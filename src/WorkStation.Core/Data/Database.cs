using Microsoft.Data.Sqlite;
using WorkStation.Core.Time;

namespace WorkStation.Core.Data;

/// <summary>
/// 数据库连接、迁移、完整性检查、事务与备份。
///
/// 与 Electron 版 electron/db.ts 一一对应，语义逐条对齐：
/// WAL 模式、外键开启、版本化迁移、启动前完整性探测、备份前 checkpoint。
/// </summary>
public static class Database
{
    /// <summary>打开数据库并设置 PRAGMA。失败时必须关闭句柄，否则文件仍被占用。</summary>
    public static SqliteConnection OpenDatabase(string filePath)
    {
        var directory = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = filePath
        }.ToString());

        try
        {
            connection.Open();
            ExecuteScalar(connection, "PRAGMA journal_mode = WAL");
            Execute(connection, "PRAGMA foreign_keys = ON");
        }
        catch
        {
            // 打开失败时必须关闭句柄，否则文件仍被占用（Windows 上改名/删除会报 EBUSY）
            try
            {
                connection.Dispose();
            }
            catch
            {
                // 关闭失败不掩盖原始错误
            }

            throw;
        }

        return connection;
    }

    public static int GetUserVersion(SqliteConnection connection)
    {
        var value = ExecuteScalar(connection, "PRAGMA user_version");
        return value is null ? 0 : Convert.ToInt32(value);
    }

    /// <summary>
    /// 按版本号升序执行待应用的迁移。
    ///
    /// 必须排序：数组顺序可能是乱的，而 user_version 是绝对值，
    /// 乱序执行会把 user_version 写低，导致下次启动重复建表而失败。
    /// </summary>
    public static void RunMigrations(SqliteConnection connection)
    {
        var current = GetUserVersion(connection);
        var pending = Migrations.All
            .Where(m => m.Version > current)
            .OrderBy(m => m.Version)
            .ToList();

        var applied = current;
        foreach (var migration in pending)
        {
            if (migration.Version <= applied)
            {
                throw new InvalidOperationException(
                    $"迁移版本号必须严格递增 (version={migration.Version} {migration.Name} <= {applied})");
            }

            using var transaction = connection.BeginTransaction(deferred: true);
            try
            {
                Execute(connection, migration.Sql, transaction);
                // version 来自我们自己的常量表，不是用户输入
                Execute(connection, $"PRAGMA user_version = {migration.Version}", transaction);
                transaction.Commit();
            }
            catch (Exception error)
            {
                try
                {
                    transaction.Rollback();
                }
                catch
                {
                    // 事务可能已被 SQLite 在致命错误（SQLITE_FULL / SQLITE_IOERR）下自动回滚，
                    // 此时 Rollback 本身会抛错；不能让它顶掉原始的迁移失败原因
                }

                throw new InvalidOperationException(
                    $"迁移失败 (version={migration.Version} {migration.Name}): {error.Message}", error);
            }

            applied = migration.Version;
        }
    }

    /// <summary>完整性检查：PRAGMA integrity_check 必须只返回一行且为 ok。</summary>
    public static bool IsHealthy(SqliteConnection connection)
    {
        var results = new List<string>();
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA integrity_check";
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            results.Add(reader.GetString(0));
        }

        return results.Count == 1 && results[0] == "ok";
    }

    /// <summary>
    /// 启动时探测数据库完整性；损坏则把原文件改名留存并重建空库。
    ///
    /// 关键：探测用独立的短连接，且必须在改名之前 Dispose ——
    /// Windows 上未关闭的句柄会让改名报 EBUSY，用户看到的是「窗口根本不弹」。
    /// </summary>
    /// <returns>损坏文件的留存路径；数据库健康时返回 null。</returns>
    public static string? RecoverIfCorrupt(string dbPath)
    {
        if (!File.Exists(dbPath)) return null;

        var healthy = false;
        try
        {
            // Pooling=false 是关键：默认连接池化下 Dispose() 只是把连接还回池子，
            // 文件句柄仍然占着，Windows 上改名会报 EBUSY —— 那正是「窗口根本不弹」的成因。
            using var probe = new SqliteConnection(new SqliteConnectionStringBuilder
            {
                DataSource = dbPath,
                Pooling = false
            }.ToString());
            probe.Open();
            healthy = IsHealthy(probe);
        }
        catch
        {
            healthy = false;
        }
        // 上面的 using 已保证探测连接在走到改名之前就真正关闭了文件句柄

        if (healthy) return null;

        var stamp = LocalTime.ToLocalDate(DateTime.Now).Replace("-", string.Empty);
        foreach (var suffix in new[] { string.Empty, "-wal", "-shm" })
        {
            var target = dbPath + suffix;
            // 副文件可能已经被 SQLite 自己在识别到主库损坏时删掉了（实测如此），跳过即可
            if (!File.Exists(target)) continue;

            try
            {
                File.Move(target, $"{dbPath}.corrupt-{stamp}{suffix}", overwrite: true);
            }
            catch (IOException)
            {
                // 文件仍被占用：跳过这一个，保留原文件总比删掉强
            }
            catch (UnauthorizedAccessException)
            {
                // 同上
            }
        }

        return $"{dbPath}.corrupt-{stamp}";
    }

    /// <summary>事务包装（无返回值）。ROLLBACK 自身抛错不得顶掉原始异常。</summary>
    public static void WithTransaction(SqliteConnection connection, Action action) =>
        WithTransaction(connection, () =>
        {
            action();
            return true;
        });

    /// <summary>事务包装。ROLLBACK 自身抛错不得顶掉原始异常。</summary>
    public static T WithTransaction<T>(SqliteConnection connection, Func<T> action)
    {
        using var transaction = connection.BeginTransaction(deferred: true);
        try
        {
            var result = action();
            transaction.Commit();
            return result;
        }
        catch
        {
            try
            {
                transaction.Rollback();
            }
            catch
            {
                // 同上：不能让它顶掉原始异常
            }

            throw;
        }
    }

    /// <summary>
    /// 备份当前库到目标文件。
    ///
    /// WAL 模式下必须先 checkpoint(TRUNCATE)，否则已提交但还在 -wal 里的事务不在主库文件里，
    /// 复制出来的库会缺数据。
    /// </summary>
    public static void BackupDatabase(SqliteConnection connection, string targetPath)
    {
        Execute(connection, "PRAGMA wal_checkpoint(TRUNCATE)");

        var source = connection.DataSource;
        if (string.IsNullOrEmpty(source) || source.Equals(":memory:", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("内存库无法备份到文件");
        }

        var directory = Path.GetDirectoryName(targetPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.Copy(source, targetPath, overwrite: true);
    }

    /// <summary>
    /// 每天首次启动时备份一次，只保留最近 keepCount 份。
    /// 备份文件名按日期排序（data-YYYY-MM-DD.db），因此最旧的排在最前。
    /// </summary>
    /// <returns>本次是否真的创建了备份（当天已存在则返回 false）。</returns>
    public static bool RunDailyBackup(SqliteConnection connection, string dbPath, int keepCount = 7)
    {
        var backupDirectory = AppPaths.BackupDirectory(dbPath);
        var today = LocalTime.ToLocalDate(DateTime.Now);
        var target = Path.Combine(backupDirectory, $"data-{today}.db");
        if (File.Exists(target)) return false;

        Directory.CreateDirectory(backupDirectory);
        BackupDatabase(connection, target);

        var files = Directory.GetFiles(backupDirectory, "data-*.db")
            .OrderBy(path => Path.GetFileName(path), StringComparer.Ordinal)
            .ToList();
        while (files.Count > keepCount)
        {
            var oldest = files[0];
            files.RemoveAt(0);
            try
            {
                File.Delete(oldest);
            }
            catch (IOException)
            {
                // 删不掉就留着，下一次启动再试
            }
        }

        return true;
    }

    private static void Execute(SqliteConnection connection, string sql, SqliteTransaction? transaction = null)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        if (transaction is not null) command.Transaction = transaction;
        command.ExecuteNonQuery();
    }

    private static object? ExecuteScalar(SqliteConnection connection, string sql, SqliteTransaction? transaction = null)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        if (transaction is not null) command.Transaction = transaction;
        return command.ExecuteScalar();
    }
}
