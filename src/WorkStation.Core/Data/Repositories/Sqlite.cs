using Microsoft.Data.Sqlite;

namespace WorkStation.Core.Data.Repositories;

/// <summary>
/// SQLite 命令与读取的共用小工具。
///
/// 参数一律具名绑定（@name）：实测 Microsoft.Data.Sqlite 不支持「? 占位符 + 无名参数」
/// （报 ParameterName must be set），也不支持「? 占位符 + 具名参数」
/// （报 Must add values for the following parameters），只有 @具名 占位符能正常绑定。
/// 所有 SQL 仍然只做参数绑定，不做字符串拼接。
/// </summary>
internal static class Sqlite
{
    public static SqliteCommand Command(this SqliteConnection connection, string sql, SqliteTransaction? transaction = null)
    {
        var command = connection.CreateCommand();
        command.CommandText = sql;
        // 连接上有活动事务时，命令必须显式挂上同一个事务对象，否则 Microsoft.Data.Sqlite
        // 会抛 "Transaction required" / "Transaction mismatch"
        if (transaction is not null) command.Transaction = transaction;
        return command;
    }

    /// <summary>按顺序绑定具名参数。null 一律转成 DBNull（SQLite 没有 null 绑定值）。</summary>
    public static SqliteCommand Bind(this SqliteCommand command, params (string Name, object? Value)[] parameters)
    {
        foreach (var (name, value) in parameters)
        {
            command.Parameters.Add(new SqliteParameter(name, value ?? DBNull.Value));
        }

        return command;
    }

    /// <summary>读取全部行。</summary>
    public static List<T> ReadAll<T>(this SqliteCommand command, Func<SqliteDataReader, T> map)
    {
        var result = new List<T>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            result.Add(map(reader));
        }

        return result;
    }

    public static string Str(this SqliteDataReader reader, string column) =>
        reader.GetString(reader.GetOrdinal(column));

    public static string? StrOrNull(this SqliteDataReader reader, string column)
    {
        var ordinal = reader.GetOrdinal(column);
        return reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
    }

    public static int Int(this SqliteDataReader reader, string column) =>
        reader.GetInt32(reader.GetOrdinal(column));

    public static int? IntOrNull(this SqliteDataReader reader, string column)
    {
        var ordinal = reader.GetOrdinal(column);
        return reader.IsDBNull(ordinal) ? null : reader.GetInt32(ordinal);
    }

    public static double? DoubleOrNull(this SqliteDataReader reader, string column)
    {
        var ordinal = reader.GetOrdinal(column);
        return reader.IsDBNull(ordinal) ? null : reader.GetDouble(ordinal);
    }

    /// <summary>生成 @p0, @p1, ... 形式的占位符列表，并返回可绑定的参数名。</summary>
    public static (string Placeholders, string[] Names) Placeholders(string prefix, int count)
    {
        var names = Enumerable.Range(0, count).Select(i => $"{prefix}{i}").ToArray();
        return (string.Join(", ", names.Select(n => $"@{n}")), names);
    }
}
