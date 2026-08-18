using System.Collections.Generic;

using LiveCaptionsTranslator.Mac.Captions;

namespace LiveCaptionsTranslator.Mac.Models;

/// <summary>Whisper 模型的下载源偏好。</summary>
public enum ModelDownloadSource
{
    /// <summary>自动：先试 ModelScope（国内快），失败回退 Hugging Face。</summary>
    Auto,

    /// <summary>只用 ModelScope 镜像。</summary>
    ModelScope,

    /// <summary>只用 whisper.cpp 官方 Hugging Face 仓库。</summary>
    HuggingFace
}

/// <summary>应用全局设置（持久化到 JSON）。</summary>
public sealed class AppSettings
{
    /// <summary>语音识别使用的 Whisper 模型规格。</summary>
    public WhisperModel WhisperModel { get; set; } = WhisperModel.Base;

    /// <summary>模型下载源偏好。</summary>
    public ModelDownloadSource ModelSource { get; set; } = ModelDownloadSource.Auto;

    /// <summary>
    /// 自定义模型下载源前缀（可选，例如自建镜像）。
    /// 非空时优先于 <see cref="ModelSource"/> 尝试，需以 / 结尾。
    /// </summary>
    public string? CustomModelBaseUrl { get; set; }

    /// <summary>上次选择的采集设备名称（用于下次启动自动选中）。</summary>
    public string? PreferredAudioDevice { get; set; }

    /// <summary>翻译相关设置。</summary>
    public TranslationSettings Translation { get; set; } = new();

    /// <summary>按当前偏好组装模型下载源列表（按尝试顺序）。</summary>
    public IReadOnlyList<string> BuildModelBaseUrls()
    {
        var urls = new List<string>();

        if (!string.IsNullOrWhiteSpace(CustomModelBaseUrl))
        {
            string custom = CustomModelBaseUrl!.Trim();
            urls.Add(custom.EndsWith('/') ? custom : custom + "/");
        }

        switch (ModelSource)
        {
            case ModelDownloadSource.ModelScope:
                urls.Add(WhisperModelProvider.ModelScopeBaseUrl);
                break;
            case ModelDownloadSource.HuggingFace:
                urls.Add(WhisperModelProvider.HuggingFaceBaseUrl);
                break;
            default:
                urls.AddRange(WhisperModelProvider.DefaultBaseUrls);
                break;
        }

        return urls;
    }
}
