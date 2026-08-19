using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.Data.Sqlite;

using LiveCaptionsTranslator.Mac.Models;
using LiveCaptionsTranslator.Mac.Utils;

namespace LiveCaptionsTranslator.Mac.Services;

/// <summary>
/// 翻译历史存储：SQLite 数据库，位于
/// ~/Library/Application Support/LiveCaptionsTranslator/history.db。
/// 对应 Windows 版 SQLiteHistoryLogger（简化、独立实现）。
/// </summary>
public static class HistoryStore
{
    private static readonly string DatabasePath = Path.Combine(AppPaths.DataDirectory, "history.db");
    private static readonly string ConnectionString = $"Data Source={DatabasePath}";

    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static bool _initialized;

    /// <summary>确保数据库与表已创建。</summary>
    private static async Task EnsureInitializedAsync(SqliteConnection connection, CancellationToken token)
    {
        if (_initialized)
            return;

        await using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS TranslationHistory (
                Id              INTEGER PRIMARY KEY AUTOINCREMENT,
                Timestamp       INTEGER NOT NULL,
                SourceText      TEXT    NOT NULL,
                TranslatedText  TEXT    NOT NULL,
                TargetLanguage  TEXT    NOT NULL,
                EngineUsed      TEXT    NOT NULL
            );
            """;
        await command.ExecuteNonQueryAsync(token);
        _initialized = true;
    }

    private static async Task<SqliteConnection> OpenAsync(CancellationToken token)
    {
        var connection = new SqliteConnection(ConnectionString);
        await connection.OpenAsync(token);
        await EnsureInitializedAsync(connection, token);
        return connection;
    }

    /// <summary>
    /// 新增一条翻译记录。调用方（MainWindow）只在句子已结束时才记，
    /// 每条都是一个独立完整的句子，因此这里一律新增。
    /// </summary>
    public static async Task LogAsync(
        string sourceText, string translatedText, string targetLanguage, string engineUsed,
        CancellationToken token = default)
    {
        if (string.IsNullOrWhiteSpace(sourceText) || string.IsNullOrWhiteSpace(translatedText))
            return;

        await Gate.WaitAsync(token);
        try
        {
            await using var connection = await OpenAsync(token);
            await using var command = connection.CreateCommand();
            command.CommandText = """
                INSERT INTO TranslationHistory
                    (Timestamp, SourceText, TranslatedText, TargetLanguage, EngineUsed)
                VALUES (@ts, @src, @dst, @lang, @engine)
                """;
            command.Parameters.AddWithValue("@ts", DateTimeOffset.UtcNow.ToUnixTimeSeconds());
            command.Parameters.AddWithValue("@src", sourceText);
            command.Parameters.AddWithValue("@dst", translatedText);
            command.Parameters.AddWithValue("@lang", targetLanguage);
            command.Parameters.AddWithValue("@engine", engineUsed);
            await command.ExecuteNonQueryAsync(token);
        }
        finally
        {
            Gate.Release();
        }
    }

    /// <summary>按页读取历史（时间倒序）。返回记录与总页数。</summary>
    public static async Task<(List<TranslationHistoryEntry> Entries, int TotalPages)> LoadAsync(
        int page, int pageSize, string? searchText = null, CancellationToken token = default)
    {
        var entries = new List<TranslationHistoryEntry>();
        string pattern = $"%{searchText ?? string.Empty}%";

        await Gate.WaitAsync(token);
        try
        {
            await using var connection = await OpenAsync(token);

            int total;
            await using (var count = connection.CreateCommand())
            {
                count.CommandText = """
                    SELECT COUNT(*) FROM TranslationHistory
                    WHERE SourceText LIKE @search OR TranslatedText LIKE @search
                    """;
                count.Parameters.AddWithValue("@search", pattern);
                total = Convert.ToInt32(await count.ExecuteScalarAsync(token));
            }

            int totalPages = Math.Max(1, (int)Math.Ceiling(total / (double)pageSize));
            int offset = Math.Max(0, (Math.Clamp(page, 1, totalPages) - 1) * pageSize);

            await using (var command = connection.CreateCommand())
            {
                command.CommandText = """
                    SELECT Timestamp, SourceText, TranslatedText, TargetLanguage, EngineUsed
                    FROM TranslationHistory
                    WHERE SourceText LIKE @search OR TranslatedText LIKE @search
                    ORDER BY Id DESC
                    LIMIT @limit OFFSET @offset
                    """;
                command.Parameters.AddWithValue("@search", pattern);
                command.Parameters.AddWithValue("@limit", pageSize);
                command.Parameters.AddWithValue("@offset", offset);

                await using var reader = await command.ExecuteReaderAsync(token);
                while (await reader.ReadAsync(token))
                    entries.Add(ReadEntry(reader));
            }

            return (entries, totalPages);
        }
        finally
        {
            Gate.Release();
        }
    }

    /// <summary>清空全部历史。</summary>
    public static async Task ClearAsync(CancellationToken token = default)
    {
        await Gate.WaitAsync(token);
        try
        {
            await using var connection = await OpenAsync(token);
            await using var command = connection.CreateCommand();
            command.CommandText =
                "DELETE FROM TranslationHistory; " +
                "DELETE FROM sqlite_sequence WHERE name = 'TranslationHistory'";
            await command.ExecuteNonQueryAsync(token);
        }
        finally
        {
            Gate.Release();
        }
    }

    /// <summary>将全部历史导出为 CSV（UTF-8 带 BOM，便于 Excel 打开）。</summary>
    public static async Task ExportCsvAsync(string filePath, CancellationToken token = default)
    {
        await Gate.WaitAsync(token);
        try
        {
            await using var connection = await OpenAsync(token);
            await using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT Timestamp, SourceText, TranslatedText, TargetLanguage, EngineUsed
                FROM TranslationHistory
                ORDER BY Id DESC
                """;

            await using var writer = new StreamWriter(filePath, false, new UTF8Encoding(true));
            await writer.WriteLineAsync("Timestamp,SourceText,TranslatedText,TargetLanguage,EngineUsed");

            await using var reader = await command.ExecuteReaderAsync(token);
            while (await reader.ReadAsync(token))
            {
                var entry = ReadEntry(reader);
                await writer.WriteLineAsync(string.Join(',',
                    Csv(entry.Timestamp.ToString("yyyy-MM-dd HH:mm:ss")),
                    Csv(entry.SourceText),
                    Csv(entry.TranslatedText),
                    Csv(entry.TargetLanguage),
                    Csv(entry.EngineUsed)));
            }
        }
        finally
        {
            Gate.Release();
        }
    }

    private static TranslationHistoryEntry ReadEntry(SqliteDataReader reader) => new()
    {
        Timestamp = DateTimeOffset.FromUnixTimeSeconds(reader.GetInt64(0)).LocalDateTime,
        SourceText = reader.GetString(1),
        TranslatedText = reader.GetString(2),
        TargetLanguage = reader.GetString(3),
        EngineUsed = reader.GetString(4)
    };

    /// <summary>按 CSV 规则转义字段：用双引号包裹，内部双引号翻倍。</summary>
    private static string Csv(string value) =>
        $"\"{value.Replace("\"", "\"\"")}\"";
}
