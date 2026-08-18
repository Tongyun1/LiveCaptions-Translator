using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
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

        // 授权头必须挂在单个请求上。不能改 HttpClient.DefaultRequestHeaders：
        // 那个对象是共享的，而字幕翻译是并发的，一边 Clear 一边发送会导致
        // 请求丢头（进而 401）或集合被修改异常。
        using var request = new HttpRequestMessage(HttpMethod.Post, _settings.ApiUrl)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _settings.ApiKey);

        HttpResponseMessage response;
        try
        {
            response = await _client.SendAsync(request, cancellationToken);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // token 未取消却抛取消，只能是 HttpClient 自己的超时。
            // 不能靠异常文案判断，那个文案会随 .NET 版本与语言变。
            return "[ERROR] 翻译失败：请求超时（>8 秒），请更换更快的接口或检查网络。";
        }
        catch (OperationCanceledException)
        {
            // 调用方主动取消（字幕更新后旧翻译被取代）。必须向上传播，
            // 否则会被下面的兜底分支变成一条 [ERROR] 文本显示给用户。
            throw;
        }
        catch (Exception ex)
        {
            return $"[ERROR] 翻译失败：{ex.Message}";
        }

        using (response)
        {
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
}
