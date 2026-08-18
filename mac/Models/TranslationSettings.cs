using System.Collections.Generic;

namespace LiveCaptionsTranslator.Mac.Models;

/// <summary>翻译相关设置。对应 Windows 版 Setting 中与翻译有关的部分（简化、独立实现）。</summary>
public sealed class TranslationSettings
{
    /// <summary>当前翻译引擎名称，取值见 <see cref="EngineNames"/>。</summary>
    public string EngineName { get; set; } = "Google";

    /// <summary>目标语言代码（如 zh-CN、en、ja）。</summary>
    public string TargetLanguage { get; set; } = "zh-CN";

    /// <summary>LLM 翻译使用的系统提示词，{0} 会被替换为目标语言。</summary>
    public string Prompt { get; set; } =
        "As a professional simultaneous interpreter, translate the following text into {0}. " +
        "Only output the translation, without any explanation or extra content.";

    /// <summary>OpenAI 兼容接口配置。</summary>
    public OpenAISettings OpenAI { get; set; } = new();

    public static readonly IReadOnlyList<string> EngineNames = new[] { "Google", "OpenAI" };
}

/// <summary>OpenAI 兼容接口（含各类自建/第三方 LLM 网关）的配置。</summary>
public sealed class OpenAISettings
{
    public string ApiUrl { get; set; } = "https://api.openai.com/v1/chat/completions";
    public string ApiKey { get; set; } = string.Empty;
    public string ModelName { get; set; } = "gpt-4o-mini";
    public double Temperature { get; set; } = 1.0;
}
