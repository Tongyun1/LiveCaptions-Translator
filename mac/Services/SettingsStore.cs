using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

using LiveCaptionsTranslator.Mac.Models;
using LiveCaptionsTranslator.Mac.Utils;

namespace LiveCaptionsTranslator.Mac.Services;

/// <summary>
/// 设置持久化：读写 ~/Library/Application Support/LiveCaptionsTranslator/settings.json。
/// <see cref="Current"/> 为全局唯一的设置实例，界面直接编辑它，改完调用 <see cref="Save"/>。
/// </summary>
public static class SettingsStore
{
    private static readonly string FilePath = Path.Combine(AppPaths.DataDirectory, "settings.json");

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    /// <summary>全局设置实例。</summary>
    public static AppSettings Current { get; private set; } = Load();

    /// <summary>从磁盘加载设置；文件不存在或损坏时返回默认设置。</summary>
    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                string json = File.ReadAllText(FilePath);
                var loaded = JsonSerializer.Deserialize<AppSettings>(json, Options);
                if (loaded is not null)
                {
                    Current = loaded;
                    return loaded;
                }
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[SettingsStore] 读取设置失败，使用默认值: {ex.Message}");
        }

        Current = new AppSettings();
        return Current;
    }

    /// <summary>将当前设置写入磁盘。</summary>
    public static void Save()
    {
        try
        {
            string json = JsonSerializer.Serialize(Current, Options);
            File.WriteAllText(FilePath, json);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[SettingsStore] 保存设置失败: {ex.Message}");
        }
    }
}
