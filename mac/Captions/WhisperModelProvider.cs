using System;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

using LiveCaptionsTranslator.Mac.Utils;

namespace LiveCaptionsTranslator.Mac.Captions;

/// <summary>Whisper ggml 模型规格（体积/速度/精度递增）。</summary>
public enum WhisperModel
{
    Tiny,
    Base,
    Small,
    Medium
}

/// <summary>
/// 负责按需下载并缓存 Whisper ggml 模型文件。
/// 模型来自 whisper.cpp 官方 Hugging Face 仓库，首次使用时下载到本地数据目录。
/// </summary>
public static class WhisperModelProvider
{
    private const string BaseUrl = "https://huggingface.co/ggerganov/whisper.cpp/resolve/main/";

    private static string FileName(WhisperModel model) => model switch
    {
        WhisperModel.Tiny => "ggml-tiny.bin",
        WhisperModel.Base => "ggml-base.bin",
        WhisperModel.Small => "ggml-small.bin",
        WhisperModel.Medium => "ggml-medium.bin",
        _ => "ggml-base.bin"
    };

    /// <summary>返回本地模型文件路径，缺失时下载。progress 回调传入 0~1 的下载进度。</summary>
    public static async Task<string> EnsureModelAsync(
        WhisperModel model,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        string fileName = FileName(model);
        string path = Path.Combine(AppPaths.ModelsDirectory, fileName);

        if (File.Exists(path) && new FileInfo(path).Length > 0)
            return path;

        string tempPath = path + ".download";
        using var http = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };

        using (var response = await http.GetAsync(
                   BaseUrl + fileName, HttpCompletionOption.ResponseHeadersRead, cancellationToken))
        {
            response.EnsureSuccessStatusCode();
            long? total = response.Content.Headers.ContentLength;

            await using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
            await using var target = new FileStream(
                tempPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true);

            var buffer = new byte[81920];
            long readTotal = 0;
            int read;
            while ((read = await source.ReadAsync(buffer, cancellationToken)) > 0)
            {
                await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                readTotal += read;
                if (total is > 0)
                    progress?.Report((double)readTotal / total.Value);
            }
        }

        File.Move(tempPath, path, overwrite: true);
        return path;
    }
}
