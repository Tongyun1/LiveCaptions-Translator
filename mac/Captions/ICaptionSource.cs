using System;

namespace LiveCaptionsTranslator.Mac.Captions;

/// <summary>
/// 字幕来源抽象。macOS 版通过具体实现（系统音频捕获 + 语音识别）
/// 产出实时文本，供上层编排逻辑消费与翻译。
/// 这是 Windows 版 LiveCaptionsHandler（UI Automation 读屏）在 macOS 上的替代扩展点。
/// </summary>
public interface ICaptionSource
{
    /// <summary>识别到新的（或更新的）字幕文本时触发。</summary>
    event EventHandler<string>? CaptionReceived;

    /// <summary>开始捕获与识别。</summary>
    void Start();

    /// <summary>停止捕获与识别。</summary>
    void Stop();
}
