using System;

namespace LiveCaptionsTranslator.Mac.Models;

/// <summary>一条翻译历史记录。</summary>
public sealed class TranslationHistoryEntry
{
    /// <summary>记录时间（本地时区）。</summary>
    public DateTime Timestamp { get; set; }

    /// <summary>识别出的原文。</summary>
    public string SourceText { get; set; } = string.Empty;

    /// <summary>翻译结果。</summary>
    public string TranslatedText { get; set; } = string.Empty;

    /// <summary>目标语言代码。</summary>
    public string TargetLanguage { get; set; } = string.Empty;

    /// <summary>使用的翻译引擎名称。</summary>
    public string EngineUsed { get; set; } = string.Empty;

    /// <summary>列表中显示的时间文本。</summary>
    public string TimeDisplay => Timestamp.ToString("MM-dd HH:mm:ss");
}
