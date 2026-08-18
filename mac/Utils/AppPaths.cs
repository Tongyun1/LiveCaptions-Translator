using System;
using System.IO;
using System.Reflection;

namespace LiveCaptionsTranslator.Mac.Utils;

/// <summary>
/// macOS 下应用目录管理。可写数据遵循 ~/Library/Application Support 约定；
/// 同时支持读取打包在 .app 内的只读资源。
/// </summary>
public static class AppPaths
{
    /// <summary>应用数据根目录：~/Library/Application Support/LiveCaptionsTranslator。</summary>
    public static string DataDirectory { get; }

    /// <summary>Whisper 模型下载后的存放目录（可写）。</summary>
    public static string ModelsDirectory { get; }

    /// <summary>
    /// 打包在 .app 内的模型目录（Contents/Resources/models），不在 .app 中运行时为 null。
    /// 存在时优先使用，用户就无需联网下载模型。
    /// </summary>
    public static string? BundledModelsDirectory { get; }

    /// <summary>
    /// 是否作为 .app 运行。macOS 的权限系统（TCC）按 bundle 身份记账，
    /// 不在 .app 中时无法申请语音识别等权限。
    /// </summary>
    public static bool IsRunningInAppBundle { get; }

    static AppPaths()
    {
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        DataDirectory = Path.Combine(home, "Library", "Application Support", "LiveCaptionsTranslator");
        ModelsDirectory = Path.Combine(DataDirectory, "models");

        Directory.CreateDirectory(DataDirectory);
        Directory.CreateDirectory(ModelsDirectory);

        BundledModelsDirectory = ResolveBundledModels();
        IsRunningInAppBundle = ResolveExecutableDirectory() is { } dir
            && string.Equals(Path.GetFileName(dir), "MacOS", StringComparison.Ordinal);
    }

    private static string? ResolveExecutableDirectory()
    {
        try
        {
            return Path.GetDirectoryName(Environment.ProcessPath
                ?? Assembly.GetEntryAssembly()?.Location);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// 从可执行文件位置推导 bundle 内的模型目录。
    /// .app 结构为 Contents/MacOS/可执行文件，资源则在 Contents/Resources。
    /// </summary>
    private static string? ResolveBundledModels()
    {
        try
        {
            string? exeDir = ResolveExecutableDirectory();
            if (string.IsNullOrEmpty(exeDir))
                return null;

            // .../Contents/MacOS  ->  .../Contents/Resources/models
            if (!string.Equals(Path.GetFileName(exeDir), "MacOS", StringComparison.Ordinal))
                return null;

            string? contents = Path.GetDirectoryName(exeDir);
            if (contents is null)
                return null;

            string models = Path.Combine(contents, "Resources", "models");
            return Directory.Exists(models) ? models : null;
        }
        catch
        {
            return null;
        }
    }
}
