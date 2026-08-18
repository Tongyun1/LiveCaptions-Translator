using System.Collections.Generic;

using LiveCaptionsTranslator.Mac.Captions;

namespace LiveCaptionsTranslator.Mac.Models;

/// <summary>语音识别引擎。</summary>
public enum RecognitionEngine
{
    /// <summary>
    /// macOS 系统语音识别（默认）：免模型、延迟极低。
    /// 离线仅支持英文与中文，其余语言由苹果服务器识别（音频会上传）；
    /// 且需以 .app 方式运行以取得授权。
    /// </summary>
    AppleSpeech,

    /// <summary>本地 Whisper：约 99 种语言全离线，需下载模型。</summary>
    Whisper
}

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
    /// <summary>
    /// 使用哪个语音识别引擎。默认系统识别：免模型、开箱即用。
    /// 从源码运行（非 .app）时拿不到系统授权，届时会自动退回 Whisper。
    /// </summary>
    public RecognitionEngine Engine { get; set; } = RecognitionEngine.AppleSpeech;

    /// <summary>系统语音识别引擎识别的语言（如 en-US、zh-CN）。</summary>
    public string AppleSpeechLocale { get; set; } = "en-US";

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
