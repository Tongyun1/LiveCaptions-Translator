using System;
using System.Threading;
using System.Threading.Tasks;

using SoundFlow.Structs;

namespace LiveCaptionsTranslator.Mac.Captions;

/// <summary>
/// 一次字幕更新。
/// </summary>
/// <param name="Text">当前这段话的最新内容。同一段话会随识别推进反复更新。</param>
/// <param name="IsFinal">
/// 这段话是否已结束。为 true 后，下一次 <see cref="Text"/> 属于新的一段话。
///
/// 边界由字幕源负责判定，而不是让上层从文本去猜：只有源才同时拥有音频与
/// 识别器的内部信息。靠标点、文本相似度等手段反推边界都已实测不可靠
/// （系统识别往往不给标点，而识别器会回头修正已输出的词）。
/// </param>
public readonly record struct CaptionUpdate(string Text, bool IsFinal);

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
    /// <summary>识别到新内容时触发。可能在后台线程触发。</summary>
    event EventHandler<CaptionUpdate>? CaptionReceived;

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
