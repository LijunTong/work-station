using Microsoft.Data.Sqlite;
using WorkStation.Core.Data;
using WorkStation.Core.Services;

namespace WorkStation.App;

/// <summary>
/// 启动装配：打开数据库 → 迁移 → 每日备份 → 构造服务层。
///
/// 顺序不能颠倒：
/// 1. 先做损坏探测（RecoverIfCorrupt 用独立短连接，必须在正式连接建立前跑完，
///    Windows 上否则会因句柄占用改名失败）；
/// 2. 再打开正式连接并迁移；
/// 3. 备份放在迁移之后，保证当天的备份是「已升级」的结构，可被当前版本直接打开。
/// </summary>
public sealed class AppServices : IDisposable
{
    private readonly SqliteConnection _connection;

    private AppServices(
        SqliteConnection connection,
        WorkStationService service,
        string databasePath,
        string? recoveredFrom,
        bool backupCreated)
    {
        _connection = connection;
        Service = service;
        DatabasePath = databasePath;
        RecoveredFrom = recoveredFrom;
        BackupCreated = backupCreated;
    }

    public WorkStationService Service { get; }

    public string DatabasePath { get; }

    public string BackupDirectory => AppPaths.BackupDirectory(DatabasePath);

    /// <summary>启动时检测到数据库损坏并留存的原文件路径；健康时为 null。</summary>
    public string? RecoveredFrom { get; }

    /// <summary>本次启动是否新建了当日备份。</summary>
    public bool BackupCreated { get; }

    public static AppServices Start()
    {
        var databasePath = AppPaths.ResolveDatabasePath();
        var recoveredFrom = Database.RecoverIfCorrupt(databasePath);

        var connection = Database.OpenDatabase(databasePath);
        try
        {
            Database.RunMigrations(connection);

            var backupCreated = false;
            try
            {
                backupCreated = Database.RunDailyBackup(connection, databasePath);
            }
            catch (Exception)
            {
                // 备份失败不该挡住启动：数据库本身是好的，用户还能继续用。
                // 真正的写入失败会在后续操作里以 Result 形式暴露。
            }

            var service = new WorkStationService(
                connection,
                databasePath,
                typeof(AppServices).Assembly.GetName().Version?.ToString(3) ?? "0.1.0");

            return new AppServices(connection, service, databasePath, recoveredFrom, backupCreated);
        }
        catch
        {
            connection.Dispose();
            throw;
        }
    }

    public void Dispose() => _connection.Dispose();
}
