using System.Text.Json;
using Microsoft.Data.Sqlite;
using WorkStation.Core.Data;
using WorkStation.Core.Data.Repositories;
using WorkStation.Core.Models;
using WorkStation.Core.Reminders;

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
    private readonly SqliteConnection _connection;
    private readonly ProjectsRepository _projects;
    private readonly TasksRepository _tasks;
    private readonly TagsRepository _tags;
    private readonly SettingsRepository _settings;
    private readonly RemindersRepository _reminders;
    private readonly string _databasePath;
    private readonly string _userDataPath;
    private readonly string _version;

    public WorkStationService(SqliteConnection connection, string databasePath, string version = "0.1.0")
    {
        _connection = connection;
        _projects = new ProjectsRepository(connection);
        _tasks = new TasksRepository(connection);
        _tags = new TagsRepository(connection);
        _settings = new SettingsRepository(connection);
        _reminders = new RemindersRepository(connection);
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

    /// <summary>
    /// 新任务的默认提前提醒量（分钟）：结束前一小时。
    /// 表单里的「提前提醒」默认也选这一档，两边必须一致 ——
    /// 表单显示「提前 1 小时」而库里是 0，提醒就不会响。
    /// </summary>
    public const int DefaultRemindBeforeMinutes = 60;

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

            // 默认提醒：结束前一小时。没给截止时间就没有提醒可排，remind_before 只是备着。
            // 显式传 0 表示「不提醒」，那个值原样保留。
            var effective = input with
            {
                RemindBefore = input.RemindBefore ?? DefaultRemindBeforeMinutes
            };

            var task = _tasks.Create(effective);
            // 截止时间与提前量一起落库后，立刻把提醒排上 —— 提醒的排期只由这一个入口产生
            _reminders.SyncTaskDue(task);
            return task;
        });

    public Result<TaskItem?> TasksUpdate(string id, UpdateTaskInput patch) =>
        Guard("更新任务", () =>
        {
            if (patch.Title.HasValue && string.IsNullOrWhiteSpace(patch.Title.Value))
            {
                throw new ArgumentException("任务标题不能为空");
            }

            var task = _tasks.Update(id, patch);
            if (task is not null) _reminders.SyncTaskDue(task);
            return task;
        });

    public Result<TaskItem?> TasksSetStatus(string id, TaskStatus status) =>
        Guard("更新任务状态", () =>
        {
            var task = _tasks.SetStatus(id, status);
            // 标记完成/取消后不该再提醒：这条路径不经过 TasksUpdate，必须单独同步
            if (task is not null) _reminders.SyncTaskDue(task);
            return task;
        });

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

    // ---------- 提醒 ----------

    public Result<IReadOnlyList<ReminderItem>> RemindersList(ReminderScope scope = ReminderScope.Upcoming) =>
        Guard("读取提醒列表", () => _reminders.List(scope));

    public Result<ReminderItem?> RemindersGet(string id) => Guard("读取提醒", () => _reminders.Get(id));

    public Result<ReminderItem> RemindersCreate(CreateReminderInput input) =>
        Guard("创建提醒", () => _reminders.Create(input));

    public Result<bool> RemindersSetStatus(string id, ReminderStatus status) =>
        Guard("更新提醒状态", () => _reminders.SetStatus(id, status));

    public Result<bool> RemindersReschedule(string id, string remindAt) =>
        Guard("重排提醒", () => _reminders.Reschedule(id, remindAt));

    public Result<bool> RemindersRemove(string id) => Guard("删除提醒", () => _reminders.Remove(id));

    public Result<ReminderCounts> RemindersCounts() => Guard("统计提醒", () => _reminders.Counts());

    public Result<IReadOnlyList<ReminderItem>> RemindersDue(string nowLocalDateTime) =>
        Guard("扫描到点提醒", () => _reminders.ListDue(nowLocalDateTime));

    /// <summary>启动时对全部任务跑一遍同步，修掉「应用没开时改了截止时间」这类漂移。</summary>
    public Result<int> RemindersSync() =>
        Guard("同步任务提醒", () =>
        {
            var tasks = _tasks.List();
            var changed = 0;
            foreach (var task in tasks)
            {
                if (_reminders.SyncTaskDue(task)) changed++;
            }

            return changed;
        });

    public Result<DailyReviewSettings> DailyReviewGet() =>
        Guard("读取站会提醒设置", () => _settings.Get("daily_review")?.Deserialize<DailyReviewSettings>() ?? new DailyReviewSettings());

    public Result<bool> DailyReviewSet(DailyReviewSettings settings) =>
        Guard("保存站会提醒设置", () =>
        {
            if (!DailyReviewPlanner.IsValidTime(settings.Time))
            {
                throw new ArgumentException("站会提醒时间应形如 09:00");
            }

            _settings.Set("daily_review", settings);
            return true;
        });

    public Result<string?> DailyReviewLastFired() =>
        Guard("读取站会提醒记录", () =>
        {
            var raw = _settings.GetString("daily_review_last", string.Empty);
            return string.IsNullOrEmpty(raw) ? null : raw;
        });

    public Result<bool> DailyReviewMarkFired(string date) =>
        Guard("记录站会提醒", () =>
        {
            _settings.Set("daily_review_last", date);
            return true;
        });

    /// <summary>站会提醒的三个数字，按调度时刻实时算。</summary>
    public Result<DailyReviewCounts> DailyReviewCounts(string nowLocalDateTime) =>
        Guard("统计站会提醒数据", () =>
            DailyReviewPlanner.ComputeCounts(_tasks.List(), nowLocalDateTime));

    public Result<bool> CloseToTrayGet() =>
        Guard("读取窗口关闭行为", () => _settings.GetBoolean("close_to_tray", true));

    public Result<bool> CloseToTraySet(bool value) =>
        Guard("保存窗口关闭行为", () =>
        {
            _settings.Set("close_to_tray", value);
            return true;
        });

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

    /// <summary>
    /// 更改数据保存目录：把当前数据库整体复制到新目录（先 checkpoint，WAL 里的提交不会丢），
    /// 并把新目录写进引导配置（app-config.json）。重启后应用从新目录打开数据。
    /// 旧目录的库文件保留不动 —— 用户想回退还有原件。
    /// </summary>
    public Result<string> ChangeDataDirectory(string directory, string? configPath = null) =>
        Guard("更改数据目录", () =>
        {
            if (string.IsNullOrWhiteSpace(directory)) throw new ArgumentException("目录不能为空");

            var full = Path.GetFullPath(directory);
            Directory.CreateDirectory(full);

            var target = Path.Combine(full, AppPaths.DatabaseFileName);
            if (!string.Equals(Path.GetFullPath(_databasePath), target, StringComparison.OrdinalIgnoreCase))
            {
                Database.BackupDatabase(_connection, target);
            }

            AppPaths.WriteDataDirectoryOverride(full, configPath);
            return full;
        });

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
