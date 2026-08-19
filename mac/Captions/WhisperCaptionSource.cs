using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using LiveCaptionsTranslator.Mac.Audio;

using SoundFlow.Structs;

using Whisper.net;

namespace LiveCaptionsTranslator.Mac.Captions;

/// <summary>
/// macOS 版字幕来源：从系统音频设备采集 PCM，用 Whisper 本地识别为文本。
/// 这是 Windows 版 LiveCaptionsHandler（UI Automation 读屏）的 macOS 等价实现。
///
/// 识别策略：将连续语音累积为一段“话语”，每隔固定间隔对当前话语做一次识别并
/// 发出中间结果（CaptionReceived）；检测到静音间隔或话语过长时提交并清空缓冲，
/// 开始下一段话语。注意 CaptionReceived 在后台线程触发，UI 订阅方需自行切回 UI 线程。
/// </summary>
public sealed class WhisperCaptionSource : ICaptionSource
{
    private const int SampleRate = SystemAudioCapture.SampleRate;
    private const int IntervalMs = 700;                    // 识别节奏
    private const int MinProcessSamples = SampleRate / 2;  // 至少 0.5s 才识别
    private const int MaxUtteranceSamples = SampleRate * 15; // 单段话语上限 15s
    private const double SilenceRmsThreshold = 0.012;      // 静音判定阈值（均方根）
    private const float VoicePeakThreshold = 0.02f;        // 含语音判定阈值（峰值）
    private const int SilenceCommitMs = 800;               // 静音超过该时长则提交

    private readonly WhisperModel _model;
    private readonly string? _preferredDeviceNameContains;
    private readonly IReadOnlyList<string>? _modelBaseUrls;

    private readonly SystemAudioCapture _capture = new();
    private readonly List<float> _utterance = new();
    private readonly object _bufferLock = new();

    private WhisperFactory? _factory;
    private WhisperProcessor? _processor;
    private CancellationTokenSource? _cts;
    private Task? _loopTask;
    private DateTime _lastVoiceUtc = DateTime.UtcNow;
    private bool _initialized;

    public WhisperCaptionSource(
        WhisperModel model = WhisperModel.Base,
        string? preferredDeviceNameContains = "BlackHole",
        IReadOnlyList<string>? modelBaseUrls = null)
    {
        _model = model;
        _preferredDeviceNameContains = preferredDeviceNameContains;
        _modelBaseUrls = modelBaseUrls;
    }

    /// <summary>识别到新内容时触发。</summary>
    public event EventHandler<CaptionUpdate>? CaptionReceived;

    /// <summary>用户显式选择的采集设备；为 null 时按名称自动解析。</summary>
    public DeviceInfo? SelectedDevice { get; set; }

    /// <summary>可用采集设备列表（供 UI 让用户选择）。</summary>
    public DeviceInfo[] GetCaptureDevices() => _capture.GetCaptureDevices();

    /// <summary>下载并加载 Whisper 模型。必须在 Start 之前调用一次。</summary>
    public async Task InitializeAsync(
        IProgress<double>? downloadProgress = null,
        CancellationToken cancellationToken = default)
    {
        if (_initialized)
            return;

        string modelPath = await WhisperModelProvider.EnsureModelAsync(
            _model, _modelBaseUrls, downloadProgress, cancellationToken);

        _factory = WhisperFactory.FromPath(modelPath);
        _processor = _factory.CreateBuilder()
            .WithLanguage("auto")
            .WithThreads(Math.Max(1, Environment.ProcessorCount - 1))
            .Build();

        _initialized = true;
    }

    public void Start()
    {
        if (!_initialized || _processor is null)
            throw new InvalidOperationException("请先调用 InitializeAsync 加载模型。");
        if (_cts is not null)
            return;

        lock (_bufferLock)
            _utterance.Clear();

        _cts = new CancellationTokenSource();
        _capture.SamplesAvailable += OnSamplesAvailable;

        DeviceInfo? device = SelectedDevice ?? _capture.ResolveSystemAudioDevice(_preferredDeviceNameContains);
        _capture.Start(device);

        _loopTask = Task.Run(() => RecognizeLoopAsync(_cts.Token));
    }

    public void Stop()
    {
        if (_cts is null)
            return;

        _capture.SamplesAvailable -= OnSamplesAvailable;
        _capture.Stop();

        _cts.Cancel();
        try { _loopTask?.Wait(TimeSpan.FromSeconds(2)); }
        catch (AggregateException) { /* 忽略取消异常 */ }

        _cts.Dispose();
        _cts = null;
        _loopTask = null;
    }

    private void OnSamplesAvailable(float[] samples)
    {
        double sumSquares = 0;
        for (int i = 0; i < samples.Length; i++)
            sumSquares += samples[i] * samples[i];
        double rms = samples.Length > 0 ? Math.Sqrt(sumSquares / samples.Length) : 0;

        lock (_bufferLock)
        {
            _utterance.AddRange(samples);
            if (rms >= SilenceRmsThreshold)
                _lastVoiceUtc = DateTime.UtcNow;
        }
    }

    private async Task RecognizeLoopAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(IntervalMs, token);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            try
            {
                float[] snapshot;
                bool commit;
                lock (_bufferLock)
                {
                    if (_utterance.Count < MinProcessSamples)
                        continue;

                    snapshot = _utterance.ToArray();
                    double silenceMs = (DateTime.UtcNow - _lastVoiceUtc).TotalMilliseconds;
                    commit = silenceMs > SilenceCommitMs || snapshot.Length >= MaxUtteranceSamples;
                }

                // 跳过纯静音片段：避免 Whisper 对静音产生幻觉文本，也减少无谓计算
                string text = string.Empty;
                if (HasVoice(snapshot))
                    text = await TranscribeAsync(snapshot, token);

                if (commit)
                {
                    lock (_bufferLock)
                    {
                        // 仅移除已识别部分，保留识别期间新到达的样本
                        if (_utterance.Count >= snapshot.Length)
                            _utterance.RemoveRange(0, snapshot.Length);
                        else
                            _utterance.Clear();
                    }
                }

                // commit 就是这段话的边界：要么静音足够长，要么长度到顶。
                // 上层靠这个信号决定历史是新增还是覆写，不用自己从文本去猜。
                if (!string.IsNullOrWhiteSpace(text))
                    CaptionReceived?.Invoke(this, new CaptionUpdate(text, commit));
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                // 关键：单次识别失败绝不能终止整个循环，否则字幕会永久卡住。
                Console.Error.WriteLine($"[WhisperCaptionSource] 识别循环异常，已跳过本次: {ex.Message}");
            }
        }
    }

    /// <summary>判断一段样本是否含有语音（用峰值粗判，纯静音的峰值接近 0）。</summary>
    private static bool HasVoice(float[] samples)
    {
        float peak = 0f;
        for (int i = 0; i < samples.Length; i++)
        {
            float a = Math.Abs(samples[i]);
            if (a > peak)
                peak = a;
        }
        return peak >= VoicePeakThreshold;
    }

    private async Task<string> TranscribeAsync(float[] samples, CancellationToken token)
    {
        if (_processor is null)
            return string.Empty;

        var sb = new StringBuilder();
        await foreach (SegmentData segment in _processor.ProcessAsync(samples, token))
            sb.Append(segment.Text);

        return sb.ToString().Trim();
    }

    public void Dispose()
    {
        Stop();
        _processor?.Dispose();
        _factory?.Dispose();
        _capture.Dispose();
    }
}
