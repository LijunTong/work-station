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
        // 外键必须显式开启：SQLite 默认关闭外键约束，
        // 级联删除（删项目连带删任务、删标签解除关联）与外键报错都依赖它。
        using (var pragma = connection.CreateCommand())
        {
            pragma.CommandText = "PRAGMA foreign_keys = ON";
            pragma.ExecuteNonQuery();
        }

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

    /// <summary>执行一条 SQL（不取结果）。事务内调用时必须把事务对象传进来。</summary>
    public static void Execute(SqliteConnection connection, string sql, SqliteTransaction? transaction = null)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        // 活动事务期间，Microsoft.Data.Sqlite 要求每条命令显式挂上同一个事务对象
        if (transaction is not null) command.Transaction = transaction;
        command.ExecuteNonQuery();
    }
}
