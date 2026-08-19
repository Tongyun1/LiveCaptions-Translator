using System;
using System.Collections.Generic;
using System.Runtime.Versioning;
using System.Threading;
using System.Threading.Tasks;

using LiveCaptionsTranslator.Mac.Audio;
using LiveCaptionsTranslator.Mac.Utils;

using SoundFlow.Structs;

namespace LiveCaptionsTranslator.Mac.Captions;

/// <summary>
/// 基于 macOS 系统语音识别（SFSpeechRecognizer）的字幕来源：无需下载模型，
/// 英语/中文可完全离线，识别延迟极低。
///
/// 与 <see cref="WhisperCaptionSource"/> 的取舍：本实现语言覆盖少（离线仅 en-*、zh-CN，
/// 其余语言由苹果服务器识别，音频会上传），且必须作为 .app 运行以取得系统授权。
/// </summary>
[SupportedOSPlatform("macos")]
public sealed class AppleSpeechCaptionSource : ICaptionSource
{
    // ---- 边界判定 ----
    //
    // 系统识别返回的是“从任务开始到现在”的累计文本。关键做法：**不按句切任务**，
    // 而是用一个已提交文本前缀记录已入史部分，当前句 = 累计文本减去已提交前缀。
    //
    // 为何不切任务：切断 SFSpeechRecognitionTask 再重建会产生空档，空档期间喂入的
    // 音频被丢掉 —— 表现为“下一句开头的词不见了”（已实测）。任务只在 45 秒上限时才切。
    //
    // 边界信号靠“文本多久没变”（还在喂音频但识别文本不再增长 = 说话人停下了）。
    // 不用苹果词级时间戳（实测中间结果的时间戳是占位值），也不用音量（背景音乐会让音量永远高于阈值）。
    //
    // 停顿阈值自适应：不同说话人语速与停顿不同。不去学习说话人统计（那是一个反馈环），
    // 而是让“所需停顿”随当前句长线性缩短：句子越长，越短的停顿就切。
    // 这样慢说话人在清晰停顿处切，快说话人（很少停顿）则在句子变长后用短停顿切，
    // 句长自然控制在合理区间。

    /// <summary>低于此长度不单独成句（避免 laugh. / OK 这种碎片），继续攒。</summary>
    private const int MinCommitChars = 12;

    /// <summary>接近此长度时只需最短停顿就切，把句长拉回合理区间。</summary>
    private const int SoftMaxChars = 140;

    /// <summary>硬上限：一直不停时强制切出一段。</summary>
    private const int MaxUtteranceChars = 160;

    /// <summary>短句需要的停顿（充分确认说话人真的停了）。</summary>
    private static readonly TimeSpan LongPause = TimeSpan.FromMilliseconds(1400);

    /// <summary>长句只需这么短的停顿（一个换气就切）。</summary>
    private static readonly TimeSpan ShortPause = TimeSpan.FromMilliseconds(400);

    /// <summary>
    /// 单个识别任务的时长上限。系统对一次任务有约 1 分钟限制，
    /// 到点必须换（这是唯一会丢少量音频的地方），否则识别会静默停止。
    /// </summary>
    private static readonly TimeSpan TaskLifetime = TimeSpan.FromSeconds(45);

    // ---- 音频预滚 ----
    //
    // 切断任务后，新任务有约 200~400ms 预热延迟，这段时间喂入的音频识不出来，
    // 表现为“下一句开头的词丢了”。留一个最近音频的环形缓冲，切换时先把这段
    // 补喂给新任务，把空档里的词找回来。因为是在停顿处切，这段基本是“停顿尾声
    // + 下一句刚冒头”，不会把上一句的词带回来（上一句在 1 秒多前就结束了）。

    /// <summary>预滚时长（毫秒）。太短补不回首词，太长会把上一句尾巴带进新句。</summary>
    private const int PreRollMs = 500;

    private readonly Queue<float[]> _preRoll = new();
    private int _preRollSamples;
    private static int PreRollCapacity => SystemAudioCapture.SampleRate * PreRollMs / 1000;

    private readonly SystemAudioCapture _capture = new();
    private readonly string _localeId;
    private readonly string? _preferredDeviceNameContains;
    private readonly bool _preferOnDevice;
    private readonly object _lock = new();

    private IntPtr _recognizer;
    private IntPtr _request;
    private IntPtr _task;

    /// <summary>本任务已喂入的音频时长（秒）。用采样数算，不受系统调度抖动影响。</summary>
    private double _audioSeconds;

    /// <summary>
    /// 当前任务的识别文本。因为每句切断一次任务，一个任务就是一句话，
    /// 所以它直接就是“当前句”——无需减前缀、也不会被跨句重排污染。
    /// 显示与入史用的是同一个值。
    /// </summary>
    private string _currentText = string.Empty;

    /// <summary>当前文本上一次发生变化时的音频时间点（秒）。</summary>
    private double _lastChangeSeconds;

    private bool _initialized;
    private bool _running;

    /// <summary>当前活跃实例。系统回调是静态的，用它把文本路由回实例。</summary>
    private static AppleSpeechCaptionSource? _active;

    public AppleSpeechCaptionSource(
        string localeId = "en-US",
        string? preferredDeviceNameContains = "BlackHole",
        bool preferOnDevice = true)
    {
        _localeId = localeId;
        _preferredDeviceNameContains = preferredDeviceNameContains;
        _preferOnDevice = preferOnDevice;
    }

    /// <inheritdoc/>
    public event EventHandler<CaptionUpdate>? CaptionReceived;


    /// <inheritdoc/>
    public DeviceInfo? SelectedDevice { get; set; }

    /// <summary>该语言当前是否能离线识别（false 表示会走苹果服务器）。</summary>
    public bool IsOnDevice { get; private set; }

    /// <inheritdoc/>
    public DeviceInfo[] GetCaptureDevices() => _capture.GetCaptureDevices();

    /// <inheritdoc/>
    public async Task InitializeAsync(
        IProgress<double>? progress = null, CancellationToken cancellationToken = default)
    {
        if (_initialized)
            return;

        if (!AppPaths.IsRunningInAppBundle)
        {
            throw new InvalidOperationException(
                "系统语音识别需要以 .app 方式启动才能取得授权。\n" +
                "请先运行 mac/package-app.sh 打包，然后打开生成的 .app；" +
                "或在设置里改用 Whisper 引擎。");
        }

        AppleSpeechInterop.EnsureFrameworksLoaded();

        long status = AppleSpeechInterop.AuthorizationStatus();
        if (status == 0)
        {
            AppleSpeechInterop.RequestAuthorization();

            // 系统会弹出授权对话框，等用户选择；回调与轮询双重取值
            for (int i = 0; i < 120; i++)
            {
                await Task.Delay(500, cancellationToken);

                long callback = AppleSpeechInterop.AuthorizationCallbackResult();
                if (callback >= 0)
                {
                    status = callback;
                    break;
                }

                status = AppleSpeechInterop.AuthorizationStatus();
                if (status != 0)
                    break;
            }
        }

        if (status != 3)
        {
            throw new InvalidOperationException(status switch
            {
                1 => "语音识别权限被拒绝。请到「系统设置 → 隐私与安全性 → 语音识别」中允许本应用。",
                2 => "语音识别在此设备上受限，无法使用。",
                _ => "等待语音识别授权超时。若未看到授权对话框，请确认是双击 .app 启动的，" +
                     "或到「系统设置 → 隐私与安全性 → 语音识别」手动开启。"
            });
        }

        _recognizer = AppleSpeechInterop.CreateRecognizer(_localeId);
        if (_recognizer == IntPtr.Zero)
            throw new InvalidOperationException($"系统语音识别不支持语言 {_localeId}。");
        if (!AppleSpeechInterop.IsAvailable(_recognizer))
            throw new InvalidOperationException(
                $"语言 {_localeId} 的识别器当前不可用（可能需要联网，或语言包尚未就绪）。");

        IsOnDevice = _preferOnDevice && AppleSpeechInterop.SupportsOnDevice(_recognizer);
        _initialized = true;
    }

    /// <inheritdoc/>
    public void Start()
    {
        if (!_initialized || _running)
            return;

        _active = this;
        AppleSpeechInterop.TranscriptionReceived += OnTranscription;

        StartNewTask();

        _capture.SamplesAvailable += OnSamples;
        _capture.Start(SelectedDevice ?? _capture.ResolveSystemAudioDevice(_preferredDeviceNameContains));
        _running = true;
    }

    /// <inheritdoc/>
    public void Stop()
    {
        if (!_running)
            return;
        _running = false;

        _capture.SamplesAvailable -= OnSamples;
        _capture.Stop();

        AppleSpeechInterop.TranscriptionReceived -= OnTranscription;
        if (_active == this)
            _active = null;

        lock (_lock)
        {
            FinishCurrentTask();
            _preRoll.Clear();
            _preRollSamples = 0;
        }
    }

    private void OnSamples(float[] samples)
    {
        if (!_running || samples.Length == 0)
            return;

        // 音频回调跑在 miniaudio 的线程上，没有现成的 autorelease 池，
        // 不自己开一个的话系统内部产生的 autorelease 对象会一直累积。
        IntPtr pool = AppleSpeechInterop.BeginAutoreleasePool();
        string? finalized = null;
        try
        {
            lock (_lock)
            {
                _audioSeconds += (double)samples.Length / SystemAudioCapture.SampleRate;

                // 到达边界（停顿/长度）或系统时长上限：定稿当前句并**切断任务**。
                // 切断后该句被识别器定稿、不再重排，新任务从零开始，从根上避免跨句重叠。
                if (_audioSeconds >= TaskLifetime.TotalSeconds || ShouldCommit())
                {
                    finalized = _currentText;
                    FinishCurrentTask();
                    StartNewTask();
                    // 把最近的音频补喂新任务，补回预热空档里丢掉的首词
                    foreach (float[] batch in _preRoll)
                        AppleSpeechInterop.AppendSamples(_request, batch, batch.Length);
                }

                AppleSpeechInterop.AppendSamples(_request, samples, samples.Length);
                PushPreRoll(samples);
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[AppleSpeechCaptionSource] 追加音频失败: {ex.Message}");
        }
        finally
        {
            AppleSpeechInterop.DrainAutoreleasePool(pool);
        }

        if (!string.IsNullOrWhiteSpace(finalized))
            CaptionReceived?.Invoke(this, new CaptionUpdate(finalized!, true));
    }

    /// <summary>当前句是否该切断（定稿）。调用方需持有 <see cref="_lock"/>。</summary>
    private bool ShouldCommit()
    {
        int len = _currentText.Length;
        if (len == 0)
            return false;

        // 硬上限：不管有没有停顿都得切
        if (len >= MaxUtteranceChars)
            return true;

        // 太短不单独成句，继续攒
        if (len < MinCommitChars)
            return false;

        // 所需停顿随句长线性缩短：短句要 LongPause，接近 SoftMax 时降到 ShortPause
        double t = (double)(len - MinCommitChars) / (SoftMaxChars - MinCommitChars);
        t = Math.Clamp(t, 0.0, 1.0);
        double needSec = LongPause.TotalSeconds - t * (LongPause.TotalSeconds - ShortPause.TotalSeconds);

        return _audioSeconds - _lastChangeSeconds > needSec;
    }

    /// <summary>把一批样本存入预滚环形缓冲，超出时长上限就丢最旧的。调用方需持有 <see cref="_lock"/>。</summary>
    private void PushPreRoll(float[] samples)
    {
        _preRoll.Enqueue(samples);
        _preRollSamples += samples.Length;
        while (_preRollSamples > PreRollCapacity && _preRoll.Count > 1)
            _preRollSamples -= _preRoll.Dequeue().Length;
    }

    /// <summary>创建新的识别请求与任务。调用方需持有 <see cref="_lock"/>。</summary>
    private void StartNewTask()
    {
        _request = AppleSpeechInterop.CreateBufferRequest(IsOnDevice);
        _task = AppleSpeechInterop.StartTask(_recognizer, _request);
        _audioSeconds = 0;
        _lastChangeSeconds = 0;
        _currentText = string.Empty;
    }

    /// <summary>结束当前请求与任务。调用方需持有 <see cref="_lock"/>。</summary>
    private void FinishCurrentTask()
    {
        if (_task != IntPtr.Zero)
        {
            AppleSpeechInterop.CancelTask(_task);
            // 任务对象来自 recognitionTaskWithRequest:delegate:，不归我们持有，
            // 因此只丢句柄、绝不能 release（否则过释放崩溃）。
            _task = IntPtr.Zero;
        }
        if (_request != IntPtr.Zero)
        {
            AppleSpeechInterop.EndAudio(_request);
            // 请求是 alloc/init 得来的，归我们持有；每 45 秒轮换一次，不释放会累积。
            AppleSpeechInterop.Release(_request);
            _request = IntPtr.Zero;
        }
    }

    private void OnTranscription(string text, bool isFinal)
    {
        // 静态事件可能被非活跃实例收到，只有当前实例才转发
        if (!ReferenceEquals(_active, this) || !_running)
            return;

        lock (_lock)
        {
            // 一个任务就是一句话，识别文本直接就是当前句。
            // 文本变了才刷新“最后变化时间”；不再变 = 说话人停下了。
            if (!string.Equals(text, _currentText, StringComparison.Ordinal))
            {
                _currentText = text;
                _lastChangeSeconds = _audioSeconds;
            }
        }

        // 实时显示（非最终）；定稿时由 OnSamples 单独以 IsFinal 发出
        if (!string.IsNullOrWhiteSpace(text))
            CaptionReceived?.Invoke(this, new CaptionUpdate(text, false));
    }

    public void Dispose()
    {
        Stop();
        _capture.Dispose();

        // 识别器是 alloc/init 得来的，归我们持有
        AppleSpeechInterop.Release(_recognizer);
        _recognizer = IntPtr.Zero;
        _initialized = false;
    }
}
