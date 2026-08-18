using System;
using System.IO;

namespace LiveCaptionsTranslator.Mac.Utils;

/// <summary>
/// macOS 下应用数据目录管理，遵循 ~/Library/Application Support 约定。
/// 用于存放 Whisper 模型、设置、历史数据库等。
/// </summary>
public static class AppPaths
{
    /// <summary>应用数据根目录：~/Library/Application Support/LiveCaptionsTranslator。</summary>
    public static string DataDirectory { get; }

    /// <summary>Whisper 模型存放目录。</summary>
    public static string ModelsDirectory { get; }

    static AppPaths()
    {
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        DataDirectory = Path.Combine(home, "Library", "Application Support", "LiveCaptionsTranslator");
        ModelsDirectory = Path.Combine(DataDirectory, "models");

        Directory.CreateDirectory(DataDirectory);
        Directory.CreateDirectory(ModelsDirectory);
    }
}
