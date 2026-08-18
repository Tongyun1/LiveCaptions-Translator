using LiveCaptionsTranslator.Mac.Captions;

namespace LiveCaptionsTranslator.Mac.Models;

/// <summary>应用全局设置（持久化到 JSON）。</summary>
public sealed class AppSettings
{
    /// <summary>语音识别使用的 Whisper 模型规格。</summary>
    public WhisperModel WhisperModel { get; set; } = WhisperModel.Base;

    /// <summary>上次选择的采集设备名称（用于下次启动自动选中）。</summary>
    public string? PreferredAudioDevice { get; set; }

    /// <summary>翻译相关设置。</summary>
    public TranslationSettings Translation { get; set; } = new();
}
