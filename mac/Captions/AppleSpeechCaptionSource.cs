using System;
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
    /// <summary>
    /// 单个识别任务的时长上限。系统对一次任务有约 1 分钟限制，
    /// 因此到点主动换任务，避免识别静默停止。
    /// </summary>
    private static readonly TimeSpan TaskLifetime = TimeSpan.FromSeconds(45);

    private readonly SystemAudioCapture _capture = new();
    private readonly string _localeId;
    private readonly string? _preferredDeviceNameContains;
    private readonly bool _preferOnDevice;
    private readonly object _lock = new();

    private IntPtr _recognizer;
    private IntPtr _request;
    private IntPtr _task;
    private DateTime _taskStartedUtc;
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
    public event EventHandler<string>? CaptionReceived;

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
            FinishCurrentTask();
    }

    private void OnSamples(float[] samples)
    {
        if (!_running || samples.Length == 0)
            return;

        // 音频回调跑在 miniaudio 的线程上，没有现成的 autorelease 池，
        // 不自己开一个的话系统内部产生的 autorelease 对象会一直累积。
        IntPtr pool = AppleSpeechInterop.BeginAutoreleasePool();
        try
        {
            lock (_lock)
            {
                // 任务接近系统时长上限时轮换，保证识别持续进行
                if (DateTime.UtcNow - _taskStartedUtc > TaskLifetime)
                {
                    FinishCurrentTask();
                    StartNewTask();
                }

                AppleSpeechInterop.AppendSamples(_request, samples, samples.Length);
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
    }

    /// <summary>创建新的识别请求与任务。调用方需持有 <see cref="_lock"/>。</summary>
    private void StartNewTask()
    {
        _request = AppleSpeechInterop.CreateBufferRequest(IsOnDevice);
        _task = AppleSpeechInterop.StartTask(_recognizer, _request);
        _taskStartedUtc = DateTime.UtcNow;
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
        CaptionReceived?.Invoke(this, text);
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
