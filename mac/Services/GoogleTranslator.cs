using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Text.Json;

namespace LiveCaptionsTranslator.Mac.Services;

/// <summary>
/// Google 免费翻译接口（无需 API Key），移植自 Windows 版 TranslateAPI.Google。
/// 使用 clients5.google.com 的轻量端点，source 固定为 auto。
/// </summary>
public sealed class GoogleTranslator : ITranslator
{
    private readonly HttpClient _client;

    public GoogleTranslator(HttpClient client) => _client = client;

    public async Task<string> TranslateAsync(
        string text, string targetLanguage, CancellationToken cancellationToken = default)
    {
        string encoded = Uri.EscapeDataString(text);
        string url = "https://clients5.google.com/translate_a/t?" +
                     "client=dict-chrome-ex&sl=auto&" +
                     $"tl={targetLanguage}&q={encoded}";

        HttpResponseMessage response;
        try
        {
            response = await _client.GetAsync(url, cancellationToken);
        }
        catch (OperationCanceledException ex) when (ex.Message.StartsWith("The request"))
        {
            return "[ERROR] 翻译失败：请求超时（>8 秒），请更换更快的接口或检查网络。";
        }
        catch (Exception ex)
        {
            return $"[ERROR] 翻译失败：{ex.Message}";
        }

        if (!response.IsSuccessStatusCode)
            return $"[ERROR] 翻译失败：HTTP {response.StatusCode}";

        string body = await response.Content.ReadAsStringAsync(cancellationToken);
        try
        {
            var parsed = JsonSerializer.Deserialize<List<List<string>>>(body);
            return parsed?[0][0] ?? "[ERROR] 翻译失败：响应为空";
        }
        catch
        {
            return "[ERROR] 翻译失败：无法解析响应";
        }
    }
}
