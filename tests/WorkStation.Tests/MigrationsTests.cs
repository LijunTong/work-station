using Microsoft.Data.Sqlite;
using WorkStation.Core.Data;

namespace WorkStation.Tests;

public class MigrationsTests
{
    [Fact]
    public void RunMigrations_CreatesAllSevenTables()
    {
        using var connection = TestDb.OpenMigrated();

        var tables = new List<string>();
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT name FROM sqlite_master WHERE type = 'table' ORDER BY name";
            using var reader = command.ExecuteReader();
            while (reader.Read()) tables.Add(reader.GetString(0));
        }

        foreach (var expected in new[]
                 {
                     "projects", "tasks", "tags", "task_tags", "reminders", "reports", "settings"
                 })
        {
            Assert.Contains(expected, tables);
        }
    }

    [Fact]
    public void RunMigrations_CreatesAllSixIndexes()
    {
        using var connection = TestDb.OpenMigrated();

        var indexes = new List<string>();
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT name FROM sqlite_master WHERE type = 'index' AND name LIKE 'idx_%' ORDER BY name";
            using var reader = command.ExecuteReader();
            while (reader.Read()) indexes.Add(reader.GetString(0));
        }

        Assert.Equal(
            new[]
            {
                "idx_reminders_due", "idx_reports_period", "idx_task_tags_tag",
                "idx_tasks_due", "idx_tasks_project", "idx_tasks_status"
            },
            indexes);
    }

    [Fact]
    public void RunMigrations_SetsUserVersionToLatest()
    {
        using var connection = TestDb.OpenMigrated();
        Assert.Equal(Migrations.All.Max(m => m.Version), Database.GetUserVersion(connection));
    }

    /// <summary>任务有起止两头：开始时间是迁移 v2 加的列。</summary>
    [Fact]
    public void RunMigrations_AddsTaskStartAtColumn()
    {
        using var connection = TestDb.OpenMigrated();

        var columns = new List<string>();
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "PRAGMA table_info(tasks)";
            using var reader = command.ExecuteReader();
            while (reader.Read()) columns.Add(reader.GetString(1));
        }

        Assert.Contains("start_at", columns);
        Assert.Contains("due_at", columns);
    }

    [Fact]
    public void RunMigrations_IsIdempotent()
    {
        using var connection = TestDb.OpenMigrated();
        var version = Database.GetUserVersion(connection);

        // 再跑一次不应报错，也不应改变版本号或破坏表结构
        Database.RunMigrations(connection);

        Assert.Equal(version, Database.GetUserVersion(connection));
        TestDb.Execute(connection, "INSERT INTO settings (key, value) VALUES ('k', 'v')");
        Assert.Equal(1L, TestDb.Scalar(connection, "SELECT COUNT(*) FROM settings"));
    }

    [Fact]
    public void ForeignKeys_AreActuallyEnforced()
    {
        // 不用 PRAGMA 读值来断言（那是恒真断言），而是插入一条外键不存在的任务，
        // 期望被数据库拒绝
        using var connection = TestDb.OpenMigrated();

        var error = Assert.Throws<SqliteException>(() => TestDb.Execute(connection, """
            INSERT INTO tasks (id, project_id, title, notes, status, priority, remind_before,
                               sort_order, created_at, updated_at)
            VALUES ('t1', 'missing-project', '任务', '', 'todo', 'none', 0, 0, '2026-11-05T00:00:00.000Z', '2026-11-05T00:00:00.000Z')
            """));

        Assert.Contains("FOREIGN KEY", error.Message);
    }

    [Fact]
    public void CheckConstraints_RejectInvalidStatus()
    {
        using var connection = TestDb.OpenMigrated();

        Assert.Throws<SqliteException>(() => TestDb.Execute(connection, """
            INSERT INTO tasks (id, title, notes, status, priority, remind_before,
                               sort_order, created_at, updated_at)
            VALUES ('t1', '任务', '', 'not-a-status', 'none', 0, 0, '2026-11-05T00:00:00.000Z', '2026-11-05T00:00:00.000Z')
            """));
    }

    [Fact]
    public void CascadeDelete_RemovesChildTasksWhenProjectRemoved()
    {
        using var connection = TestDb.OpenMigrated();
        TestDb.Execute(connection, """
            INSERT INTO projects (id, name, description, status, priority, progress_mode,
                                  sort_order, created_at, updated_at)
            VALUES ('p1', '项目', '', 'planning', 'none', 'auto', 0, '2026-11-05T00:00:00.000Z', '2026-11-05T00:00:00.000Z')
            """);
        TestDb.Execute(connection, """
            INSERT INTO tasks (id, project_id, title, notes, status, priority, remind_before,
                               sort_order, created_at, updated_at)
            VALUES ('t1', 'p1', '任务', '', 'todo', 'none', 0, 0, '2026-11-05T00:00:00.000Z', '2026-11-05T00:00:00.000Z')
            """);

        TestDb.Execute(connection, "DELETE FROM projects WHERE id = 'p1'");

        Assert.Equal(0L, TestDb.Scalar(connection, "SELECT COUNT(*) FROM tasks"));
    }

    /// <summary>日报是新增的类型：迁移 v3 之后 kind='daily' 必须能写进库。</summary>
    [Fact]
    public void RunMigrations_AcceptsDailyReportKind()
    {
        using var connection = TestDb.OpenMigrated();

        TestDb.Execute(connection, """
            INSERT INTO reports (id, kind, period_start, period_end, created_at, updated_at)
            VALUES ('r-daily', 'daily', '2026-11-08', '2026-11-08', '2026-11-08T00:00:00.000Z', '2026-11-08T00:00:00.000Z')
            """);

        Assert.Equal("daily", TestDb.Scalar(connection, "SELECT kind FROM reports WHERE id = 'r-daily'"));
    }

    /// <summary>一个周期只允许一条报告：唯一索引必须真的拦得住。</summary>
    [Fact]
    public void RunMigrations_RejectsTwoReportsInSamePeriod()
    {
        using var connection = TestDb.OpenMigrated();

        TestDb.Execute(connection, """
            INSERT INTO reports (id, kind, period_start, period_end, created_at, updated_at)
            VALUES ('r-1', 'daily', '2026-11-08', '2026-11-08', '2026-11-08T00:00:00.000Z', '2026-11-08T00:00:00.000Z')
            """);

        var error = Assert.Throws<SqliteException>(() => TestDb.Execute(connection, """
            INSERT INTO reports (id, kind, period_start, period_end, created_at, updated_at)
            VALUES ('r-2', 'daily', '2026-11-08', '2026-11-08', '2026-11-08T00:00:00.000Z', '2026-11-08T00:00:00.000Z')
            """));

        Assert.Contains("UNIQUE", error.Message);
    }

    /// <summary>不同类型的同一周期互不冲突：日报与周报可以落在同一天。</summary>
    [Fact]
    public void RunMigrations_AllowsSamePeriodAcrossDifferentKinds()
    {
        using var connection = TestDb.OpenMigrated();

        TestDb.Execute(connection, """
            INSERT INTO reports (id, kind, period_start, period_end, created_at, updated_at)
            VALUES ('r-1', 'daily', '2026-11-02', '2026-11-02', '2026-11-02T00:00:00.000Z', '2026-11-02T00:00:00.000Z')
            """);
        TestDb.Execute(connection, """
            INSERT INTO reports (id, kind, period_start, period_end, created_at, updated_at)
            VALUES ('r-2', 'weekly', '2026-11-02', '2026-11-08', '2026-11-02T00:00:00.000Z', '2026-11-02T00:00:00.000Z')
            """);

        Assert.Equal(2L, TestDb.Scalar(connection, "SELECT COUNT(*) FROM reports"));
    }

    /// <summary>老库升上来不能丢数据：v1/v2 建出来的报告行迁移后还在。</summary>
    [Fact]
    public void RunMigrations_UpgradesOldDatabaseWithoutLosingReports()
    {
        using var connection = OpenOldDatabaseAtVersion2();

        TestDb.Execute(connection, """
            INSERT INTO reports (id, kind, period_start, period_end, created_at, updated_at)
            VALUES ('r-old', 'weekly', '2026-11-02', '2026-11-08', '2026-11-02T00:00:00.000Z', '2026-11-02T00:00:00.000Z')
            """);

        Database.RunMigrations(connection);

        Assert.Equal(3, Database.GetUserVersion(connection));
        Assert.Equal("weekly", TestDb.Scalar(connection, "SELECT kind FROM reports WHERE id = 'r-old'"));
        Assert.Equal("2026-11-02", TestDb.Scalar(connection, "SELECT period_start FROM reports WHERE id = 'r-old'"));
    }

    /// <summary>
    /// 老库里同一周期有重复行时，唯一索引建不起来会拖垮迁移 ——
    /// 迁移按 (kind, period_start) 去重并保留最早的一行。
    /// </summary>
    [Fact]
    public void RunMigrations_DeduplicatesReportsWhenUpgradingOldDatabase()
    {
        using var connection = OpenOldDatabaseAtVersion2();

        TestDb.Execute(connection, """
            INSERT INTO reports (id, kind, period_start, period_end, created_at, updated_at)
            VALUES ('r-old-1', 'weekly', '2026-11-02', '2026-11-08', '2026-11-02T00:00:00.000Z', '2026-11-02T00:00:00.000Z')
            """);
        TestDb.Execute(connection, """
            INSERT INTO reports (id, kind, period_start, period_end, created_at, updated_at)
            VALUES ('r-old-2', 'weekly', '2026-11-02', '2026-11-08', '2026-11-03T00:00:00.000Z', '2026-11-03T00:00:00.000Z')
            """);

        Database.RunMigrations(connection);

        Assert.Equal(1L, TestDb.Scalar(connection, "SELECT COUNT(*) FROM reports"));
        Assert.Equal("r-old-1", TestDb.Scalar(connection, "SELECT id FROM reports"));
    }

    /// <summary>造一个只跑了 v1/v2 迁移的老库，用来验证 v3 的升级路径。</summary>
    private static SqliteConnection OpenOldDatabaseAtVersion2()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        using (var pragma = connection.CreateCommand())
        {
            pragma.CommandText = "PRAGMA foreign_keys = ON";
            pragma.ExecuteNonQuery();
        }

        foreach (var migration in Migrations.All.Where(m => m.Version <= 2).OrderBy(m => m.Version))
        {
            TestDb.Execute(connection, migration.Sql);
            TestDb.Execute(connection, $"PRAGMA user_version = {migration.Version}");
        }

        return connection;
    }
}
