using Microsoft.Data.Sqlite;
using WorkStation.Core.Models;
using WorkStation.Core.Time;

namespace WorkStation.Core.Data.Repositories;

/// <summary>
/// 任务仓储。SQL 与语义逐条对齐 Electron 版 electron/repositories/tasks.ts，
/// 并修掉三处已记录的缺陷：
/// <list type="bullet">
///   <item>#5 includeDone=false 现在同时排除 done 与 canceled，且 status 与 includeDone 的优先级写死</item>
///   <item>#6 读取侧（dueBefore / dueAfter / countOverdue）也做格式护栏，非法格式直接抛错</item>
///   <item>#7 countByStatus 固定五个键，未知 status 忽略，不写进结果</item>
/// </list>
/// </summary>
public sealed class TasksRepository
{
    private static readonly TaskStatus[] AllStatuses =
    {
        TaskStatus.Todo, TaskStatus.Doing, TaskStatus.Blocked, TaskStatus.Done, TaskStatus.Canceled
    };

    private readonly SqliteConnection _connection;
    private readonly Func<string> _nowIso;

    /// <param name="nowIso">
    /// 时间戳来源，默认 <see cref="LocalTime.NowIso"/>。
    /// 测试必须注入可控的时间源：「状态迁移时已有时间戳保留原值」这类语义，
    /// 只有在每次调用都返回不同值时才能被真正验证 —— 真实时钟的毫秒精度
    /// 可能让两次调用落在同一毫秒，保留与重写得到同一个字符串，断言就成了恒真。
    /// </param>
    public TasksRepository(SqliteConnection connection, Func<string>? nowIso = null)
    {
        _connection = connection;
        _nowIso = nowIso ?? (() => LocalTime.NowIso());
    }

    private static TaskItem MapRow(SqliteDataReader reader) => new()
    {
        Id = reader.Str("id"),
        ProjectId = reader.StrOrNull("project_id"),
        Title = reader.Str("title"),
        Notes = reader.Str("notes"),
        Status = DbEnum.ParseTaskStatus(reader.Str("status")),
        Priority = DbEnum.ParsePriority(reader.Str("priority")),
        DueAt = reader.StrOrNull("due_at"),
        RemindBefore = reader.Int("remind_before"),
        Estimate = reader.DoubleOrNull("estimate"),
        Spent = reader.DoubleOrNull("spent"),
        BlockedReason = reader.StrOrNull("blocked_reason"),
        SortOrder = reader.Int("sort_order"),
        CreatedAt = reader.Str("created_at"),
        UpdatedAt = reader.Str("updated_at"),
        StartedAt = reader.StrOrNull("started_at"),
        CompletedAt = reader.StrOrNull("completed_at")
    };

    /// <summary>
    /// due_at 参与的是本地墙钟字符串字典序比较，只有 YYYY-MM-DDTHH:mm 这种定长且本地有序的
    /// 格式才能保证「字符串序 = 时间序」。写入侧与读取侧都必须守住这个契约：
    /// 混入 ISO UTC 串会让字典序比较把当天 18:00 到期的任务判成逾期 —— 静默错判必须变成显式失败。
    /// </summary>
    private static void AssertLocalDateTime(string value, string field)
    {
        if (!LocalTime.IsLocalDateTime(value))
        {
            throw new ArgumentException(
                $"{field} 必须是本地墙钟格式 YYYY-MM-DDTHH:mm，收到：{value}", field);
        }
    }

    public IReadOnlyList<TaskItem> List(TaskFilter? filter = null)
    {
        var where = new List<string>();
        var parameters = new List<(string, object?)>();

        if (filter is not null)
        {
            // projectId 三态：None = 不过滤，Some(null) = 只看独立任务，Some(id) = 只看该项目的
            if (filter.ProjectId.HasValue)
            {
                if (filter.ProjectId.Value is null)
                {
                    where.Add("project_id IS NULL");
                }
                else
                {
                    where.Add("project_id = @projectId");
                    parameters.Add(("projectId", filter.ProjectId.Value));
                }
            }

            // status 与 includeDone 同时给定时以 status 为准（includeDone 被忽略）
            if (filter.Status is { Count: > 0 })
            {
                var (placeholders, names) = Sqlite.Placeholders("s", filter.Status.Count);
                where.Add($"status IN ({placeholders})");
                for (var i = 0; i < names.Length; i++)
                {
                    parameters.Add((names[i], filter.Status[i].ToDb()));
                }
            }
            else if (filter.IncludeDone == false)
            {
                // 排除 done 与 canceled —— canceled 的任务同样不该出现在「未完成」视图里
                var open = AllStatuses.Where(s => s != TaskStatus.Done && s != TaskStatus.Canceled).ToArray();
                var (placeholders, names) = Sqlite.Placeholders("o", open.Length);
                where.Add($"status IN ({placeholders})");
                for (var i = 0; i < names.Length; i++)
                {
                    parameters.Add((names[i], open[i].ToDb()));
                }
            }

            if (filter.Priority is { Count: > 0 })
            {
                var (placeholders, names) = Sqlite.Placeholders("p", filter.Priority.Count);
                where.Add($"priority IN ({placeholders})");
                for (var i = 0; i < names.Length; i++)
                {
                    parameters.Add((names[i], filter.Priority[i].ToDb()));
                }
            }

            // 按标签筛选：task_tags 只有 task→tags 方向，筛选必须用 EXISTS 反查子查询
            if (filter.TagIds is { Count: > 0 })
            {
                var (placeholders, names) = Sqlite.Placeholders("t", filter.TagIds.Count);
                where.Add(
                    $"EXISTS (SELECT 1 FROM task_tags tt WHERE tt.task_id = tasks.id AND tt.tag_id IN ({placeholders}))");
                for (var i = 0; i < names.Length; i++)
                {
                    parameters.Add((names[i], filter.TagIds[i]));
                }
            }

            if (!string.IsNullOrEmpty(filter.DueBefore))
            {
                AssertLocalDateTime(filter.DueBefore, nameof(TaskFilter.DueBefore));
                where.Add("due_at IS NOT NULL AND due_at < @dueBefore");
                parameters.Add(("dueBefore", filter.DueBefore));
            }

            if (!string.IsNullOrEmpty(filter.DueAfter))
            {
                AssertLocalDateTime(filter.DueAfter, nameof(TaskFilter.DueAfter));
                where.Add("due_at IS NOT NULL AND due_at >= @dueAfter");
                parameters.Add(("dueAfter", filter.DueAfter));
            }

            if (!string.IsNullOrEmpty(filter.Search))
            {
                where.Add("(title LIKE @search OR notes LIKE @search)");
                parameters.Add(("search", $"%{filter.Search}%"));
            }
        }

        var sql = "SELECT * FROM tasks";
        if (where.Count > 0)
        {
            sql += " WHERE " + string.Join(" AND ", where);
        }

        sql += " ORDER BY sort_order ASC, created_at ASC";

        using var command = _connection.Command(sql).Bind(parameters.ToArray());
        return command.ReadAll(MapRow);
    }

    public TaskItem? Get(string id)
    {
        using var command = _connection.Command("SELECT * FROM tasks WHERE id = @id").Bind(("id", id));
        return command.ReadAll(MapRow).FirstOrDefault();
    }

    public TaskItem Create(CreateTaskInput input)
    {
        if (!string.IsNullOrEmpty(input.DueAt))
        {
            AssertLocalDateTime(input.DueAt, nameof(CreateTaskInput.DueAt));
        }

        var id = Ids.NewId();
        var now = _nowIso();
        var status = input.Status ?? TaskStatus.Todo;

        using var command = _connection.Command("""
            INSERT INTO tasks (
              id, project_id, title, notes, status, priority, due_at, remind_before,
              estimate, spent, blocked_reason, sort_order, created_at, updated_at, started_at, completed_at
            ) VALUES (
              @id, @projectId, @title, @notes, @status, @priority, @dueAt, @remindBefore,
              @estimate, @spent, @blockedReason, @sortOrder, @createdAt, @updatedAt, @startedAt, @completedAt
            )
            """).Bind(
            ("id", id),
            ("projectId", input.ProjectId),
            ("title", input.Title),
            ("notes", input.Notes ?? string.Empty),
            ("status", status.ToDb()),
            ("priority", (input.Priority ?? Priority.None).ToDb()),
            ("dueAt", input.DueAt),
            ("remindBefore", input.RemindBefore ?? 0),
            ("estimate", input.Estimate),
            ("spent", input.Spent),
            ("blockedReason", input.BlockedReason),
            ("sortOrder", 0),
            ("createdAt", now),
            ("updatedAt", now),
            ("startedAt", status == TaskStatus.Doing ? now : null),
            ("completedAt", status == TaskStatus.Done ? now : null));
        command.ExecuteNonQuery();

        return Get(id) ?? throw new InvalidOperationException("任务创建后读取失败");
    }

    /// <summary>
    /// 更新任务。setStatus 与 update 共享同一套状态迁移与时间戳语义。
    /// </summary>
    public TaskItem? Update(string id, UpdateTaskInput patch)
    {
        var existing = Get(id);
        if (existing is null) return null;

        // 只校验本次传入的新值：没传保留原值，传 null 是清空截止时间，两者都无需校验
        if (patch.DueAt.HasValue && patch.DueAt.Value is not null)
        {
            AssertLocalDateTime(patch.DueAt.Value, nameof(UpdateTaskInput.DueAt));
        }

        var nextStatus = patch.Status.HasValue ? patch.Status.Value : existing.Status;
        var now = LocalTime.NowIso();
        // 首次进入 doing 时写 started_at，已有则保留原值
        var startedAt = nextStatus == TaskStatus.Doing ? existing.StartedAt ?? now : existing.StartedAt;
        // 进入 done 时写 completed_at；从 done 退回其他状态时清空
        var completedAt = nextStatus == TaskStatus.Done ? existing.CompletedAt ?? now : null;

        using var command = _connection.Command("""
            UPDATE tasks SET
              title = @title, project_id = @projectId, notes = @notes, status = @status, priority = @priority,
              due_at = @dueAt, remind_before = @remindBefore, estimate = @estimate, spent = @spent,
              blocked_reason = @blockedReason, sort_order = @sortOrder,
              updated_at = @updatedAt, started_at = @startedAt, completed_at = @completedAt
            WHERE id = @id
            """).Bind(
            ("title", patch.Title.Or(existing.Title)),
            ("projectId", patch.ProjectId.HasValue ? patch.ProjectId.Value : existing.ProjectId),
            ("notes", patch.Notes.HasValue ? patch.Notes.Value : existing.Notes),
            ("status", nextStatus.ToDb()),
            ("priority", (patch.Priority.HasValue ? patch.Priority.Value : existing.Priority).ToDb()),
            ("dueAt", patch.DueAt.HasValue ? patch.DueAt.Value : existing.DueAt),
            ("remindBefore", patch.RemindBefore.HasValue ? patch.RemindBefore.Value : existing.RemindBefore),
            ("estimate", patch.Estimate.HasValue ? patch.Estimate.Value : existing.Estimate),
            ("spent", patch.Spent.HasValue ? patch.Spent.Value : existing.Spent),
            ("blockedReason", patch.BlockedReason.HasValue ? patch.BlockedReason.Value : existing.BlockedReason),
            ("sortOrder", patch.SortOrder.HasValue ? patch.SortOrder.Value : existing.SortOrder),
            ("updatedAt", now),
            ("startedAt", startedAt),
            ("completedAt", completedAt),
            ("id", id));
        command.ExecuteNonQuery();

        return Get(id);
    }

    public TaskItem? SetStatus(string id, TaskStatus status) =>
        Update(id, new UpdateTaskInput { Status = Optional<TaskStatus>.Some(status) });

    public bool Remove(string id)
    {
        using var command = _connection.Command("DELETE FROM tasks WHERE id = @id").Bind(("id", id));
        return command.ExecuteNonQuery() > 0;
    }

    /// <summary>按状态计数。固定五个键，数据库里出现未知 status 时忽略而不是写进结果。</summary>
    public IReadOnlyDictionary<TaskStatus, int> CountByStatus()
    {
        var result = AllStatuses.ToDictionary(status => status, _ => 0);

        using var command = _connection.Command("SELECT status, COUNT(*) AS c FROM tasks GROUP BY status");
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            var raw = reader.Str("status");
            if (!Enum.TryParse<TaskStatus>(raw, ignoreCase: true, out var status)) continue;
            result[status] = reader.Int("c");
        }

        return result;
    }

    /// <summary>
    /// 逾期任务数。墙钟口径：due_at 与入参同为 YYYY-MM-DDTHH:mm 本地墙钟字符串，
    /// 字典序即时间序；已完成/已取消不计入逾期。
    /// </summary>
    public int CountOverdue(string nowLocalDateTime)
    {
        AssertLocalDateTime(nowLocalDateTime, nameof(nowLocalDateTime));

        using var command = _connection.Command("""
            SELECT COUNT(*) AS c FROM tasks
            WHERE status NOT IN ('done','canceled')
              AND due_at IS NOT NULL AND due_at < @now
            """).Bind(("now", nowLocalDateTime));
        return Convert.ToInt32(command.ExecuteScalar());
    }

    public IReadOnlyList<TaskItem> ListByProject(string projectId) =>
        List(new TaskFilter { ProjectId = Optional<string?>.Some(projectId) });
}
