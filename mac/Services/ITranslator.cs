using System.Threading;
using System.Threading.Tasks;

namespace LiveCaptionsTranslator.Mac.Services;

/// <summary>翻译引擎统一接口。返回译文；失败时返回以 [ERROR] 开头的说明。</summary>
public interface ITranslator
{
    /// <summary>将文本翻译到目标语言。</summary>
    Task<string> TranslateAsync(string text, string targetLanguage, CancellationToken cancellationToken = default);
}
