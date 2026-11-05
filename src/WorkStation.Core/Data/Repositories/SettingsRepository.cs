using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace WorkStation.Core.Data.Repositories;

/// <summary>
/// 设置仓储。语义与 Electron 版 electron/repositories/settings.ts 一致：
/// 值以 JSON 字符串存放，损坏的值当作不存在。
/// </summary>
public sealed class SettingsRepository
{
    private readonly SqliteConnection _connection;

    public SettingsRepository(SqliteConnection connection) => _connection = connection;

    private static JsonElement? Parse(string? raw)
    {
        if (raw is null) return null;
        try
        {
            using var document = JsonDocument.Parse(raw);
            return document.RootElement.Clone();
        }
        catch (JsonException)
        {
            // 损坏的值当作不存在
            return null;
        }
    }

    /// <summary>读取一个设置值。键不存在或值损坏都返回 null。</summary>
    public JsonElement? Get(string key)
    {
        using var command = _connection.Command("SELECT value FROM settings WHERE key = @key").Bind(("key", key));
        var raw = command.ExecuteScalar() as string;
        return Parse(raw);
    }

    /// <summary>写入一个设置值。null 会被存成 JSON null。</summary>
    public void Set(string key, object? value)
    {
        var json = JsonSerializer.Serialize(value);
        using var command = _connection.Command("""
            INSERT INTO settings (key, value) VALUES (@key, @value)
            ON CONFLICT(key) DO UPDATE SET value = excluded.value
            """).Bind(("key", key), ("value", json));
        command.ExecuteNonQuery();
    }

    public bool Remove(string key)
    {
        using var command = _connection.Command("DELETE FROM settings WHERE key = @key").Bind(("key", key));
        return command.ExecuteNonQuery() > 0;
    }

    /// <summary>全部设置，按键名升序。损坏的值直接跳过，不影响其他键。</summary>
    public IReadOnlyDictionary<string, JsonElement> GetAll()
    {
        var result = new SortedDictionary<string, JsonElement>(StringComparer.Ordinal);
        using var command = _connection.Command("SELECT key, value FROM settings ORDER BY key ASC");
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            var key = reader.Str("key");
            var parsed = Parse(reader.Str("value"));
            if (parsed.HasValue) result[key] = parsed.Value;
        }

        return result;
    }

    public string? GetString(string key, string fallback)
    {
        var value = Get(key);
        if (value is not { } element) return fallback;
        return element.ValueKind == JsonValueKind.String ? element.GetString() : fallback;
    }

    public bool GetBoolean(string key, bool fallback)
    {
        var value = Get(key);
        if (value is not { } element) return fallback;
        return element.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => fallback
        };
    }
}
