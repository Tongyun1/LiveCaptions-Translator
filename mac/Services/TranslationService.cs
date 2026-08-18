using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

using LiveCaptionsTranslator.Mac.Models;

namespace LiveCaptionsTranslator.Mac.Services;

/// <summary>
/// 翻译门面：根据 <see cref="TranslationSettings"/> 选择具体引擎并执行翻译。
/// 对应 Windows 版 TranslateAPI 的 TranslateFunction 分发逻辑（简化、独立实现）。
/// </summary>
public sealed class TranslationService
{
    private readonly HttpClient _client = new() { Timeout = TimeSpan.FromSeconds(8) };

    public TranslationSettings Settings { get; }

    public TranslationService(TranslationSettings? settings = null)
    {
        Settings = settings ?? new TranslationSettings();
    }

    /// <summary>按当前设置翻译文本。</summary>
    public Task<string> TranslateAsync(string text, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(text))
            return Task.FromResult(string.Empty);

        ITranslator engine = Settings.EngineName switch
        {
            "OpenAI" => new OpenAICompatibleTranslator(_client, Settings.OpenAI),
            _ => new GoogleTranslator(_client),
        };

        return engine.TranslateAsync(text, Settings.TargetLanguage, cancellationToken);
    }
}
