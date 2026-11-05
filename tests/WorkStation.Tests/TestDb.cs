using Microsoft.Data.Sqlite;
using WorkStation.Core.Data;

namespace WorkStation.Tests;

/// <summary>测试用的临时目录与内存库辅助。</summary>
internal static class TestDb
{
    /// <summary>开一个已迁移的内存库。内存库与连接同生共死，迁移必须跑在同一个连接上。</summary>
    public static SqliteConnection OpenMigrated()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        Database.RunMigrations(connection);
        return connection;
    }

    /// <summary>开一个已迁移的临时文件库，返回库与所在目录（调用方负责清理）。</summary>
    public static (SqliteConnection Connection, string Directory) OpenTempFile(string name = "ws")
    {
        var directory = Path.Combine(Path.GetTempPath(), "work-station-tests", $"{name}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var connection = Database.OpenDatabase(Path.Combine(directory, "data.db"));
        Database.RunMigrations(connection);
        return (connection, directory);
    }

    public static void DeleteDirectory(string directory)
    {
        try
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
        catch (IOException)
        {
            // Windows 上文件句柄可能还没释放，测试清理失败不影响结论
        }
    }

    /// <summary>执行一条 SQL 并返回首行首列。</summary>
    public static object? Scalar(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return command.ExecuteScalar();
    }

    /// <summary>执行一条 SQL（不取结果）。</summary>
    public static void Execute(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }
}
