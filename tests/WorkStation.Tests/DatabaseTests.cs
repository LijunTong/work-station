using WorkStation.Core.Data;

namespace WorkStation.Tests;

public class DatabaseTests
{
    [Fact]
    public void OpenDatabase_EnablesWalMode()
    {
        var (connection, directory) = TestDb.OpenTempFile();
        try
        {
            Assert.Equal("wal", TestDb.Scalar(connection, "PRAGMA journal_mode"));
        }
        finally
        {
            connection.Dispose();
            TestDb.DeleteDirectory(directory);
        }
    }

    [Fact]
    public void OpenDatabase_CreatesMissingDirectory()
    {
        var root = Path.Combine(Path.GetTempPath(), "work-station-tests", $"mkdir-{Guid.NewGuid():N}");
        var filePath = Path.Combine(root, "nested", "data.db");
        try
        {
            using var connection = Database.OpenDatabase(filePath);
            Assert.True(File.Exists(filePath));
        }
        finally
        {
            TestDb.DeleteDirectory(root);
        }
    }

    [Fact]
    public void GetUserVersion_OnFreshDatabaseIsZero()
    {
        using var connection = new Microsoft.Data.Sqlite.SqliteConnection("Data Source=:memory:");
        connection.Open();
        Assert.Equal(0, Database.GetUserVersion(connection));
    }

    [Fact]
    public void WithTransaction_CommitsOnSuccess()
    {
        using var connection = TestDb.OpenMigrated();

        Database.WithTransaction(connection, () =>
            TestDb.Execute(connection, "INSERT INTO settings (key, value) VALUES ('a', '1')"));

        Assert.Equal(1L, TestDb.Scalar(connection, "SELECT COUNT(*) FROM settings"));
    }

    [Fact]
    public void WithTransaction_RollsBackOnError()
    {
        using var connection = TestDb.OpenMigrated();

        Assert.Throws<InvalidOperationException>(() =>
            Database.WithTransaction<object?>(connection, () =>
            {
                TestDb.Execute(connection, "INSERT INTO settings (key, value) VALUES ('a', '1')");
                throw new InvalidOperationException("boom");
            }));

        Assert.Equal(0L, TestDb.Scalar(connection, "SELECT COUNT(*) FROM settings"));
    }

    [Fact]
    public void BackupDatabase_CopiesContentToTargetFile()
    {
        var (connection, directory) = TestDb.OpenTempFile();
        try
        {
            TestDb.Execute(connection, "INSERT INTO settings (key, value) VALUES ('a', '1')");
            var target = Path.Combine(directory, "backup.db");

            Database.BackupDatabase(connection, target);

            Assert.True(File.Exists(target));
            using var backup = Database.OpenDatabase(target);
            Assert.Equal(1L, TestDb.Scalar(backup, "SELECT COUNT(*) FROM settings"));
        }
        finally
        {
            connection.Dispose();
            TestDb.DeleteDirectory(directory);
        }
    }

    [Fact]
    public void RunDailyBackup_CreatesBackupOncePerDay()
    {
        var (connection, directory) = TestDb.OpenTempFile();
        try
        {
            var dbPath = Path.Combine(directory, "data.db");

            Assert.True(Database.RunDailyBackup(connection, dbPath));
            // 同一天再跑不应重复备份
            Assert.False(Database.RunDailyBackup(connection, dbPath));

            var backups = Directory.GetFiles(AppPaths.BackupDirectory(dbPath), "data-*.db");
            Assert.Single(backups);
        }
        finally
        {
            connection.Dispose();
            TestDb.DeleteDirectory(directory);
        }
    }

    [Fact]
    public void RunDailyBackup_KeepsOnlySevenBackups()
    {
        var (connection, directory) = TestDb.OpenTempFile();
        try
        {
            var dbPath = Path.Combine(directory, "data.db");
            var backupDirectory = AppPaths.BackupDirectory(dbPath);
            Directory.CreateDirectory(backupDirectory);

            // 造 9 份更早的备份，日期都比今天早
            for (var day = 1; day <= 9; day++)
            {
                var name = $"data-2026-10-{day:D2}.db";
                File.WriteAllText(Path.Combine(backupDirectory, name), name);
            }

            Database.RunDailyBackup(connection, dbPath);

            var files = Directory.GetFiles(backupDirectory, "data-*.db");
            Assert.Equal(7, files.Length);
            // 最旧的两份应被清掉
            Assert.DoesNotContain(files, path => path.EndsWith("data-2026-10-01.db", StringComparison.Ordinal));
            Assert.DoesNotContain(files, path => path.EndsWith("data-2026-10-02.db", StringComparison.Ordinal));
        }
        finally
        {
            connection.Dispose();
            TestDb.DeleteDirectory(directory);
        }
    }
}
