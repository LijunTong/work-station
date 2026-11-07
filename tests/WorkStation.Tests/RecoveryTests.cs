using WorkStation.Core.Data;

namespace WorkStation.Tests;

public class RecoveryTests
{
    [Fact]
    public void RecoverIfCorrupt_MissingFile_ReturnsNull()
    {
        var path = Path.Combine(Path.GetTempPath(), $"not-exist-{Guid.NewGuid():N}.db");
        Assert.Null(Database.RecoverIfCorrupt(path));
    }

    [Fact]
    public void RecoverIfCorrupt_HealthyDatabase_ReturnsNull()
    {
        var (connection, directory) = TestDb.OpenTempFile();
        try
        {
            var dbPath = Path.Combine(directory, "data.db");
            Assert.Null(Database.RecoverIfCorrupt(dbPath));
            Assert.True(File.Exists(dbPath));
        }
        finally
        {
            connection.Dispose();
            TestDb.DeleteDirectory(directory);
        }
    }

    [Fact]
    public void RecoverIfCorrupt_CorruptFile_IsRenamedAndOriginalRetained()
    {
        var directory = Path.Combine(Path.GetTempPath(), "work-station-tests", $"corrupt-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            var dbPath = Path.Combine(directory, "data.db");
            File.WriteAllText(dbPath, "this is definitely not a sqlite database");

            var retained = Database.RecoverIfCorrupt(dbPath);

            Assert.NotNull(retained);
            Assert.True(File.Exists(retained));
            Assert.Equal("this is definitely not a sqlite database", File.ReadAllText(retained));
            // 原路径已不存在，可以重新建库
            Assert.False(File.Exists(dbPath));
        }
        finally
        {
            TestDb.DeleteDirectory(directory);
        }
    }

    [Fact]
    public void RecoverIfCorrupt_AfterRecovery_CanCreateFreshDatabase()
    {
        var directory = Path.Combine(Path.GetTempPath(), "work-station-tests", $"recover-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            var dbPath = Path.Combine(directory, "data.db");
            File.WriteAllText(dbPath, "garbage");
            Database.RecoverIfCorrupt(dbPath);

            using var connection = Database.OpenDatabase(dbPath);
            Database.RunMigrations(connection);

            Assert.Equal(0L, TestDb.Scalar(connection, "SELECT COUNT(*) FROM projects"));
            // 迁移跑完，版本号必须等于当前迁移条数（写死数字的话每加一条迁移都要改这里）
            Assert.Equal(Migrations.All.Count, Database.GetUserVersion(connection));
        }
        finally
        {
            TestDb.DeleteDirectory(directory);
        }
    }

    [Fact]
    public void RecoverIfCorrupt_RenamesMainFileAndLeavesNoOrphanSidecars()
    {
        var directory = Path.Combine(Path.GetTempPath(), "work-station-tests", $"wal-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            var dbPath = Path.Combine(directory, "data.db");
            File.WriteAllText(dbPath, "garbage");
            File.WriteAllText(dbPath + "-wal", "wal garbage");
            File.WriteAllText(dbPath + "-shm", "shm garbage");

            var retained = Database.RecoverIfCorrupt(dbPath);

            Assert.NotNull(retained);
            // 主库必须被改名留存，原内容不丢
            Assert.True(File.Exists(retained));
            Assert.Equal("garbage", File.ReadAllText(retained));
            // 原路径空出来，可以重建新库
            Assert.False(File.Exists(dbPath));
            // wal/shm 副文件：要么被改名留存，要么被 SQLite 自己在识别到主库损坏时清掉
            // （实测：主库非数据库时 SQLite 会直接删掉这两个副文件）。
            // 无论哪种，都不允许留在原路径上。
            Assert.False(File.Exists(dbPath + "-wal"));
            Assert.False(File.Exists(dbPath + "-shm"));
        }
        finally
        {
            TestDb.DeleteDirectory(directory);
        }
    }
}
