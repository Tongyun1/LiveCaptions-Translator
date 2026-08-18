using System;
using System.Threading;
using System.Threading.Tasks;

using SoundFlow.Structs;

namespace LiveCaptionsTranslator.Mac.Captions;

/// <summary>
/// 字幕来源抽象。macOS 版通过具体实现（系统音频捕获 + 语音识别）
/// 产出实时文本，供上层编排逻辑消费与翻译。
/// 这是 Windows 版 LiveCaptionsHandler（UI Automation 读屏）在 macOS 上的替代扩展点。
///
/// 现有两种实现：本地 Whisper（<see cref="WhisperCaptionSource"/>，语言广、需模型）
/// 与 macOS 系统语音识别（<see cref="AppleSpeechCaptionSource"/>，免模型、语言少）。
/// </summary>
public interface ICaptionSource : IDisposable
{
    /// <summary>识别到新的（或更新的）字幕文本时触发。可能在后台线程触发。</summary>
    event EventHandler<string>? CaptionReceived;

    /// <summary>可用采集设备列表（供 UI 让用户选择）。</summary>
    DeviceInfo[] GetCaptureDevices();

    /// <summary>用户显式选择的采集设备；为 null 时按名称自动解析。</summary>
    DeviceInfo? SelectedDevice { get; set; }

    /// <summary>
    /// 准备识别所需资源（下载/加载模型、申请系统授权等）。必须在 <see cref="Start"/> 之前调用一次。
    /// </summary>
    /// <param name="progress">长耗时准备工作的进度，0~1；无进度概念时不回调。</param>
    Task InitializeAsync(IProgress<double>? progress = null, CancellationToken cancellationToken = default);

    /// <summary>开始捕获与识别。</summary>
    void Start();

    /// <summary>停止捕获与识别。</summary>
    void Stop();
}
