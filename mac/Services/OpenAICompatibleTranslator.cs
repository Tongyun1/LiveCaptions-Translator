using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using LiveCaptionsTranslator.Mac.Models;

namespace LiveCaptionsTranslator.Mac.Services;

/// <summary>
/// OpenAI 兼容的 Chat Completions 翻译引擎，移植自 Windows 版 TranslateAPI.OpenAI。
/// 适用于 OpenAI 及各类兼容该协议的自建/第三方 LLM 网关。
/// </summary>
public sealed class OpenAICompatibleTranslator : ITranslator
{
    private readonly HttpClient _client;
    private readonly OpenAISettings _settings;

    public OpenAICompatibleTranslator(HttpClient client, OpenAISettings settings)
    {
        _client = client;
        _settings = settings;
    }

    public async Task<string> TranslateAsync(
        string text, string targetLanguage, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_settings.ApiKey))
            return "[ERROR] 翻译失败：未配置 OpenAI API Key";

        string prompt = string.Format(
            "As a professional simultaneous interpreter, translate the following text into {0}. " +
            "Only output the translation, without any explanation.", targetLanguage);

        var requestData = new
        {
            model = _settings.ModelName,
            temperature = _settings.Temperature,
            messages = new[]
            {
                new { role = "system", content = prompt },
                new { role = "user", content = $"🔤 {text} 🔤" }
            }
        };

        string json = JsonSerializer.Serialize(requestData);
        using var content = new StringContent(json, Encoding.UTF8, "application/json");

        _client.DefaultRequestHeaders.Clear();
        _client.DefaultRequestHeaders.Add("Authorization", $"Bearer {_settings.ApiKey}");

        HttpResponseMessage response;
        try
        {
            response = await _client.PostAsync(_settings.ApiUrl, content, cancellationToken);
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
            using var doc = JsonDocument.Parse(body);
            string? output = doc.RootElement
                .GetProperty("choices")[0]
                .GetProperty("message")
                .GetProperty("content")
                .GetString();
            return string.IsNullOrWhiteSpace(output)
                ? "[ERROR] 翻译失败：响应为空"
                : output!.Trim();
        }
        catch
        {
            return "[ERROR] 翻译失败：无法解析响应";
        }
    }
}
