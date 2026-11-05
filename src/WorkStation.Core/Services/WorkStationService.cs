using System.Text.Json;
using Microsoft.Data.Sqlite;
using WorkStation.Core.Data;
using WorkStation.Core.Data.Repositories;
using WorkStation.Core.Models;

namespace WorkStation.Core.Services;

/// <summary>应用信息。替代 Electron 版 ipc.ts 的 app:info 通道。</summary>
public sealed record AppInfo(string Version, string DatabasePath, string UserDataPath);

/// <summary>
/// 服务层：UI 的唯一数据入口。
///
/// 契约沿用设计文档 §8 —— 永远不向外 throw，失败一律转成 { ok:false, error }，
/// error.code 机器可读，error.message 是可直接展示给用户的中文文案。
///
/// 数据不存在时返回 null / false（与 M1 的 Electron 版语义一致），
/// 参数非法返回 VALIDATION_FAILED，数据库写失败返回 DB_WRITE_FAILED。
/// </summary>
public sealed class WorkStationService
{
    private readonly ProjectsRepository _projects;
    private readonly TasksRepository _tasks;
    private readonly TagsRepository _tags;
    private readonly SettingsRepository _settings;
    private readonly string _databasePath;
    private readonly string _userDataPath;
    private readonly string _version;

    public WorkStationService(SqliteConnection connection, string databasePath, string version = "0.1.0")
    {
        _projects = new ProjectsRepository(connection);
        _tasks = new TasksRepository(connection);
        _tags = new TagsRepository(connection);
        _settings = new SettingsRepository(connection);
        _databasePath = databasePath;
        _userDataPath = Path.GetDirectoryName(databasePath) ?? databasePath;
        _version = version;
    }

    // ---------- 项目 ----------

    public Result<IReadOnlyList<Project>> ProjectsList(bool includeArchived = false) =>
        Guard("读取项目列表", () => _projects.List(includeArchived));

    public Result<Project?> ProjectsGet(string id) =>
        Guard("读取项目", () => _projects.Get(id));

    public Result<Project> ProjectsCreate(CreateProjectInput input) =>
        Guard("创建项目", () =>
        {
            if (string.IsNullOrWhiteSpace(input.Name))
            {
                throw new ArgumentException("项目名称不能为空");
            }

            return _projects.Create(input);
        });

    public Result<Project?> ProjectsUpdate(string id, UpdateProjectInput patch) =>
        Guard("更新项目", () =>
        {
            if (patch.Name.HasValue && string.IsNullOrWhiteSpace(patch.Name.Value))
            {
                throw new ArgumentException("项目名称不能为空");
            }

            return _projects.Update(id, patch);
        });

    public Result<bool> ProjectsArchive(string id) => Guard("归档项目", () => _projects.Archive(id));

    public Result<bool> ProjectsUnarchive(string id) => Guard("取消归档", () => _projects.Unarchive(id));

    public Result<bool> ProjectsRemove(string id) => Guard("删除项目", () => _projects.Remove(id));

    public Result<TaskCounts> ProjectsCounts(string id) => Guard("统计项目任务", () => _projects.CountTasks(id));

    // ---------- 任务 ----------

    public Result<IReadOnlyList<TaskItem>> TasksList(TaskFilter? filter = null) =>
        Guard("读取任务列表", () => _tasks.List(filter));

    public Result<TaskItem?> TasksGet(string id) => Guard("读取任务", () => _tasks.Get(id));

    public Result<TaskItem> TasksCreate(CreateTaskInput input) =>
        Guard("创建任务", () =>
        {
            if (string.IsNullOrWhiteSpace(input.Title))
            {
                throw new ArgumentException("任务标题不能为空");
            }

            return _tasks.Create(input);
        });

    public Result<TaskItem?> TasksUpdate(string id, UpdateTaskInput patch) =>
        Guard("更新任务", () =>
        {
            if (patch.Title.HasValue && string.IsNullOrWhiteSpace(patch.Title.Value))
            {
                throw new ArgumentException("任务标题不能为空");
            }

            return _tasks.Update(id, patch);
        });

    public Result<TaskItem?> TasksSetStatus(string id, TaskStatus status) =>
        Guard("更新任务状态", () => _tasks.SetStatus(id, status));

    public Result<bool> TasksRemove(string id) => Guard("删除任务", () => _tasks.Remove(id));

    public Result<IReadOnlyDictionary<TaskStatus, int>> TasksCountsByStatus() =>
        Guard("统计任务状态", () => _tasks.CountByStatus());

    public Result<int> TasksOverdueCount(string nowLocalDateTime) =>
        Guard("统计逾期任务", () => _tasks.CountOverdue(nowLocalDateTime));

    // ---------- 标签 ----------

    public Result<IReadOnlyList<Tag>> TagsList() => Guard("读取标签列表", () => _tags.List());

    public Result<Tag?> TagsGet(string id) => Guard("读取标签", () => _tags.Get(id));

    public Result<Tag> TagsCreate(CreateTagInput input) =>
        Guard("创建标签", () =>
        {
            if (string.IsNullOrWhiteSpace(input.Name))
            {
                throw new ArgumentException("标签名称不能为空");
            }

            return _tags.Create(input);
        });

    public Result<Tag?> TagsUpdate(string id, UpdateTagInput patch) =>
        Guard("更新标签", () =>
        {
            if (patch.Name.HasValue && string.IsNullOrWhiteSpace(patch.Name.Value))
            {
                throw new ArgumentException("标签名称不能为空");
            }

            return _tags.Update(id, patch);
        });

    public Result<bool> TagsRemove(string id) => Guard("删除标签", () => _tags.Remove(id));

    public Result<bool> TagsSetTaskTags(string taskId, IReadOnlyList<string> tagIds) =>
        Guard("设置任务标签", () =>
        {
            _tags.SetTaskTags(taskId, tagIds);
            return true;
        });

    public Result<IReadOnlyList<Tag>> TagsListTaskTags(string taskId) =>
        Guard("读取任务标签", () => _tags.ListTaskTags(taskId));

    // ---------- 设置 ----------

    public Result<T?> SettingsGet<T>(string key) =>
        Guard("读取设置", () =>
        {
            var element = _settings.Get(key);
            if (element is not { } value) return default(T?);
            return value.Deserialize<T>();
        });

    public Result<bool> SettingsSet(string key, object? value) =>
        Guard("写入设置", () =>
        {
            _settings.Set(key, value);
            return true;
        });

    public Result<bool> SettingsRemove(string key) => Guard("删除设置", () => _settings.Remove(key));

    public Result<IReadOnlyDictionary<string, JsonElement>> SettingsGetAll() =>
        Guard("读取全部设置", () => _settings.GetAll());

    // ---------- 应用 ----------

    public AppInfo Info() => new(_version, _databasePath, _userDataPath);

    // ---------- 错误包装 ----------

    private static Result<T> Guard<T>(string operation, Func<T> action)
    {
        try
        {
            return Result<T>.Success(action());
        }
        catch (ArgumentException error)
        {
            return Result<T>.Failure(ErrorCodes.ValidationFailed, error.Message);
        }
        catch (SqliteException error)
        {
            return Result<T>.Failure(ErrorCodes.DbWriteFailed, DescribeSqliteError(operation, error));
        }
        catch (Exception error)
        {
            return Result<T>.Failure(ErrorCodes.HandlerFailed, $"{operation}失败：{error.Message}");
        }
    }

    /// <summary>把 SQLite 底层报错翻成用户能看懂的中文。</summary>
    private static string DescribeSqliteError(string operation, SqliteException error)
    {
        var message = error.Message;
        if (message.Contains("UNIQUE constraint failed", StringComparison.OrdinalIgnoreCase))
        {
            return $"{operation}失败：名称已存在";
        }

        if (message.Contains("FOREIGN KEY constraint failed", StringComparison.OrdinalIgnoreCase))
        {
            return $"{operation}失败：关联的数据不存在";
        }

        if (message.Contains("CHECK constraint failed", StringComparison.OrdinalIgnoreCase))
        {
            return $"{operation}失败：取值不在允许范围内";
        }

        return $"{operation}失败：{message}";
    }
}
