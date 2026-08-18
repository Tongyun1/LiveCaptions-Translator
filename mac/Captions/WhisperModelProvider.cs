using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;

using LiveCaptionsTranslator.Mac.Utils;

namespace LiveCaptionsTranslator.Mac.Captions;

/// <summary>
/// Whisper ggml 模型规格。
/// Q5 后缀为量化版本：体积大幅减小（适合网络或磁盘吃紧），精度略降。
/// </summary>
public enum WhisperModel
{
    Tiny,
    TinyQ5,
    Base,
    BaseQ5,
    Small,
    SmallQ5,
    Medium,
    MediumQ5
}

/// <summary>
/// 负责按需下载并缓存 Whisper ggml 模型文件。
///
/// 支持多个下载源并按顺序回退：ModelScope 由国内 CDN 提供、速度通常更快，
/// Hugging Face 是 whisper.cpp 官方仓库。两处的文件已核对为字节一致。
/// 下载写入 .download 临时文件并支持断点续传，只有完整下载完才移动到正式路径，
/// 因此中断不会留下损坏的模型。
/// </summary>
public static class WhisperModelProvider
{
    /// <summary>ModelScope 镜像（国内 CDN，速度快）。</summary>
    public const string ModelScopeBaseUrl =
        "https://modelscope.cn/models/cjc1887415157/whisper.cpp/resolve/master/";

    /// <summary>whisper.cpp 官方 Hugging Face 仓库。</summary>
    public const string HuggingFaceBaseUrl =
        "https://huggingface.co/ggerganov/whisper.cpp/resolve/main/";

    /// <summary>默认下载顺序：先国内镜像，失败再回退官方源。</summary>
    public static readonly IReadOnlyList<string> DefaultBaseUrls =
        new[] { ModelScopeBaseUrl, HuggingFaceBaseUrl };

    /// <summary>模型对应的文件名。注意 tiny/base/small 的量化版是 q5_1，medium 只有 q5_0。</summary>
    public static string FileNameOf(WhisperModel model) => model switch
    {
        WhisperModel.Tiny => "ggml-tiny.bin",
        WhisperModel.TinyQ5 => "ggml-tiny-q5_1.bin",
        WhisperModel.Base => "ggml-base.bin",
        WhisperModel.BaseQ5 => "ggml-base-q5_1.bin",
        WhisperModel.Small => "ggml-small.bin",
        WhisperModel.SmallQ5 => "ggml-small-q5_1.bin",
        WhisperModel.Medium => "ggml-medium.bin",
        WhisperModel.MediumQ5 => "ggml-medium-q5_0.bin",
        _ => "ggml-base.bin"
    };

    /// <summary>供设置界面展示的说明（含体积与取舍）。</summary>
    public static string DescribeModel(WhisperModel model) => model switch
    {
        WhisperModel.Tiny => "Tiny — 74 MB，最快，精度最低",
        WhisperModel.TinyQ5 => "Tiny-Q5 — 31 MB，体积最小，精度最低",
        WhisperModel.Base => "Base — 141 MB，默认，速度精度均衡",
        WhisperModel.BaseQ5 => "Base-Q5 — 57 MB，Base 的压缩版，精度略降",
        WhisperModel.Small => "Small — 465 MB，更准，更慢",
        WhisperModel.SmallQ5 => "Small-Q5 — 181 MB，Small 的压缩版",
        WhisperModel.Medium => "Medium — 1.4 GB，最准，很慢",
        WhisperModel.MediumQ5 => "Medium-Q5 — 514 MB，Medium 的压缩版",
        _ => model.ToString()
    };

    /// <summary>模型在本地的完整路径（无论是否已下载）。</summary>
    public static string LocalPathOf(WhisperModel model) =>
        Path.Combine(AppPaths.ModelsDirectory, FileNameOf(model));

    /// <summary>
    /// 返回本地模型文件路径，缺失时按 <paramref name="baseUrls"/> 顺序尝试下载。
    /// progress 回调传入 0~1 的下载进度。
    /// </summary>
    /// <exception cref="IOException">所有下载源均失败，异常消息含手动放置模型的指引。</exception>
    public static async Task<string> EnsureModelAsync(
        WhisperModel model,
        IReadOnlyList<string>? baseUrls = null,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        string fileName = FileNameOf(model);
        string path = LocalPathOf(model);

        // 用户手动放好的模型同样直接采用，不再联网
        if (File.Exists(path) && new FileInfo(path).Length > 0)
            return path;

        IReadOnlyList<string> sources = baseUrls is { Count: > 0 } ? baseUrls : DefaultBaseUrls;
        string tempPath = path + ".download";
        var failures = new List<string>();

        foreach (string baseUrl in sources)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                await DownloadAsync(baseUrl + fileName, tempPath, progress, cancellationToken);
                File.Move(tempPath, path, overwrite: true);
                return path;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                failures.Add($"{new Uri(baseUrl).Host}: {ex.Message}");
            }
        }

        throw new IOException(
            $"模型 {fileName} 下载失败。\n" +
            string.Join("\n", failures) + "\n\n" +
            $"可手动下载后放入：{AppPaths.ModelsDirectory}\n" +
            $"下载地址：{ModelScopeBaseUrl}{fileName}");
    }

    /// <summary>下载单个文件到临时路径，若临时文件已存在则尝试断点续传。</summary>
    private static async Task DownloadAsync(
        string url, string tempPath, IProgress<double>? progress, CancellationToken cancellationToken)
    {
        long offset = File.Exists(tempPath) ? new FileInfo(tempPath).Length : 0;

        using var http = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
        // ModelScope 等 CDN 对不带 User-Agent 的请求返回 403，HttpClient 默认不发该头
        http.DefaultRequestHeaders.UserAgent.ParseAdd("LiveCaptionsTranslator-Mac/1.0");

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        if (offset > 0)
            request.Headers.Range = new RangeHeaderValue(offset, null);

        using var response = await http.SendAsync(
            request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

        // 请求了续传但服务端返回完整内容（或续传偏移无效），则从头下载
        bool resuming = offset > 0 && response.StatusCode == HttpStatusCode.PartialContent;
        if (offset > 0 && !resuming)
            offset = 0;

        if (response.StatusCode == HttpStatusCode.RequestedRangeNotSatisfiable)
        {
            // 临时文件比远端更大，说明它已失效，删掉重来
            File.Delete(tempPath);
            throw new IOException("续传偏移无效，已清理临时文件，请重试");
        }
        response.EnsureSuccessStatusCode();

        long? total = response.Content.Headers.ContentLength is { } length
            ? offset + length
            : null;

        await using (var source = await response.Content.ReadAsStreamAsync(cancellationToken))
        await using (var target = new FileStream(
            tempPath,
            resuming ? FileMode.Append : FileMode.Create,
            FileAccess.Write, FileShare.None, 81920, useAsync: true))
        {
            var buffer = new byte[81920];
            long written = offset;
            int read;
            while ((read = await source.ReadAsync(buffer, cancellationToken)) > 0)
            {
                await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                written += read;
                if (total is > 0)
                    progress?.Report((double)written / total.Value);
            }
        }

        // 校验完整性：长度对不上说明被截断，删掉以免下次误当成有效缓存续传
        if (total is > 0 && new FileInfo(tempPath).Length != total.Value)
        {
            File.Delete(tempPath);
            throw new IOException($"下载不完整（期望 {total} 字节）");
        }
    }
}
