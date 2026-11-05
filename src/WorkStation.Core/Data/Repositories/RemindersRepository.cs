using Microsoft.Data.Sqlite;
using WorkStation.Core.Models;
using WorkStation.Core.Reminders;
using WorkStation.Core.Time;

namespace WorkStation.Core.Data.Repositories;

/// <summary>
/// 提醒仓储。三类提醒统一落在 reminders 表，调度器只有一条扫描路径：
/// 「status = pending 且 remind_at &lt;= 现在」。
///
/// 任务截止提醒不单独维护：保存任务、启动时各同步一次，
/// 让 remind_at 始终等于 <c>due_at − remind_before</c>，改了截止时间不会留下旧提醒。
/// </summary>
public sealed class RemindersRepository
{
    private readonly SqliteConnection _connection;
    private readonly Func<string> _nowIso;

    public RemindersRepository(SqliteConnection connection, Func<string>? nowIso = null)
    {
        _connection = connection;
        _nowIso = nowIso ?? (() => LocalTime.NowIso());
    }

    private const string SelectSql = """
        SELECT r.*,
               t.due_at  AS task_due_at,
               t.status  AS task_status,
               t.title   AS task_title,
               p.name    AS project_name
        FROM reminders r
        LEFT JOIN tasks t    ON t.id = r.task_id
        LEFT JOIN projects p ON p.id = t.project_id
        """;

    private static ReminderItem MapRow(SqliteDataReader reader)
    {
        var kind = DbEnum.ParseReminderKind(reader.Str("kind"));
        var taskStatus = reader.StrOrNull("task_status") is { } rawStatus
            ? DbEnum.ParseTaskStatus(rawStatus)
            : (TaskStatus?)null;

        return new ReminderItem
        {
            Id = reader.Str("id"),
            Kind = kind,
            TaskId = reader.StrOrNull("task_id"),
            // 任务类提醒跟随任务当前标题：任务改名后列表不该还挂着旧名字
            Title = (kind == ReminderKind.TaskDue ? reader.StrOrNull("task_title") : null)
                    ?? reader.Str("title"),
            RemindAt = reader.Str("remind_at"),
            RepeatRule = DbEnum.ParseRepeatRule(reader.Str("repeat_rule")),
            Status = DbEnum.ParseReminderStatus(reader.Str("status")),
            FiredAt = reader.StrOrNull("fired_at"),
            ProjectName = reader.StrOrNull("project_name"),
            TaskDueAt = reader.StrOrNull("task_due_at"),
            TaskState = taskStatus
        };
    }

    public IReadOnlyList<ReminderItem> List(ReminderScope scope)
    {
        var (where, order, limit) = scope switch
        {
            ReminderScope.Upcoming => ("r.status = 'pending'", "r.remind_at ASC", string.Empty),
            ReminderScope.Triggered => ("r.status = 'fired'", "r.remind_at ASC", string.Empty),
            // 已处理只留最近 20 条：提醒是「用完就忘」的东西，堆成历史列表反而没人看
            _ => ("r.status IN ('done','dismissed','failed')", "r.updated_at DESC", "LIMIT 20")
        };

        var sql = $"{SelectSql} WHERE {where} ORDER BY {order} {limit}";
        using var command = _connection.Command(sql);
        return command.ReadAll(MapRow);
    }

    /// <summary>到点且还没弹过的提醒。字符串是本地墙钟定长格式，字典序即时间序。</summary>
    public IReadOnlyList<ReminderItem> ListDue(string nowLocalDateTime)
    {
        if (!LocalTime.IsLocalDateTime(nowLocalDateTime))
        {
            throw new ArgumentException(
                $"now 必须是本地墙钟格式 YYYY-MM-DDTHH:mm，收到：{nowLocalDateTime}", nameof(nowLocalDateTime));
        }

        using var command = _connection.Command(
            $"{SelectSql} WHERE r.status = 'pending' AND r.remind_at <= @now ORDER BY r.remind_at ASC")
            .Bind(("now", nowLocalDateTime));
        return command.ReadAll(MapRow);
    }

    public ReminderItem? Get(string id)
    {
        using var command = _connection.Command($"{SelectSql} WHERE r.id = @id").Bind(("id", id));
        return command.ReadAll(MapRow).FirstOrDefault();
    }

    public ReminderCounts Counts()
    {
        using var command = _connection.Command("""
            SELECT
              (SELECT COUNT(*) FROM reminders WHERE status = 'fired')   AS unhandled,
              (SELECT COUNT(*) FROM reminders WHERE status = 'pending') AS upcoming
            """);
        using var reader = command.ExecuteReader();
        return reader.Read()
            ? new ReminderCounts { Unhandled = reader.Int("unhandled"), Upcoming = reader.Int("upcoming") }
            : new ReminderCounts();
    }

    public ReminderItem Create(CreateReminderInput input)
    {
        if (string.IsNullOrWhiteSpace(input.Title))
        {
            throw new ArgumentException("提醒内容不能为空");
        }

        if (!LocalTime.IsLocalDateTime(input.RemindAt))
        {
            throw new ArgumentException(
                $"提醒时间必须是本地墙钟格式 YYYY-MM-DDTHH:mm，收到：{input.RemindAt}", nameof(input.RemindAt));
        }

        var id = Ids.NewId();
        var now = _nowIso();

        using var command = _connection.Command("""
            INSERT INTO reminders (
              id, kind, task_id, title, remind_at, repeat_rule, channel, status, fired_at, created_at, updated_at
            ) VALUES (
              @id, 'custom', NULL, @title, @remindAt, @repeat, 'inapp', 'pending', NULL, @now, @now
            )
            """).Bind(
            ("id", id),
            ("title", input.Title.Trim()),
            ("remindAt", input.RemindAt),
            ("repeat", input.RepeatRule.ToDb()),
            ("now", now));
        command.ExecuteNonQuery();

        return Get(id) ?? throw new InvalidOperationException("提醒创建后读取失败");
    }

    /// <summary>改状态。标为已触发时一并记下触发时刻，其余状态清空。</summary>
    public bool SetStatus(string id, ReminderStatus status)
    {
        var now = _nowIso();

        using var command = _connection.Command("""
            UPDATE reminders
            SET status = @status, fired_at = @firedAt, updated_at = @now
            WHERE id = @id
            """).Bind(
            ("status", status.ToDb()),
            ("firedAt", status is ReminderStatus.Fired or ReminderStatus.Done or ReminderStatus.Dismissed ? now : null),
            ("now", now),
            ("id", id));
        return command.ExecuteNonQuery() > 0;
    }

    /// <summary>重复提醒触发后把下一次排上，保持 pending。</summary>
    public bool Reschedule(string id, string nextRemindAt)
    {
        if (!LocalTime.IsLocalDateTime(nextRemindAt))
        {
            throw new ArgumentException(
                $"提醒时间必须是本地墙钟格式 YYYY-MM-DDTHH:mm，收到：{nextRemindAt}", nameof(nextRemindAt));
        }

        using var command = _connection.Command("""
            UPDATE reminders
            SET remind_at = @remindAt, status = 'pending', updated_at = @now
            WHERE id = @id
            """).Bind(
            ("remindAt", nextRemindAt),
            ("now", _nowIso()),
            ("id", id));
        return command.ExecuteNonQuery() > 0;
    }

    /// <summary>「稍后再提醒」：时间往后推，状态回到 pending。</summary>
    public bool Snooze(string id, string remindAt) => Reschedule(id, remindAt);

    public bool Remove(string id)
    {
        using var command = _connection.Command("DELETE FROM reminders WHERE id = @id").Bind(("id", id));
        return command.ExecuteNonQuery() > 0;
    }

    /// <summary>
    /// 按任务当前状态同步它的截止提醒。返回是否真的改了库 ——
    /// 启动时会对所有任务跑一遍，值没变就不该产生任何写入。
    /// </summary>
    public bool SyncTaskDue(TaskItem task)
    {
        var desired = task.Status is TaskStatus.Done or TaskStatus.Canceled
            ? null
            : ReminderPlanner.ComputeTaskRemindAt(task.DueAt, task.RemindBefore);

        var existing = ListPendingTaskDue(task.Id);

        if (desired is null)
        {
            return DeletePendingTaskDue(task.Id) > 0;
        }

        // 已经有一条时间和期望一致的待触发记录：不重写，避免每次启动都把 updated_at 抹一遍
        if (existing.Count == 1 && string.Equals(existing[0].RemindAt, desired, StringComparison.Ordinal))
        {
            return false;
        }

        var now = _nowIso();
        using var transaction = _connection.BeginTransaction();

        using (var delete = _connection.Command(
                   "DELETE FROM reminders WHERE kind = 'task_due' AND task_id = @taskId AND status = 'pending'",
                   transaction).Bind(("taskId", task.Id)))
        {
            delete.ExecuteNonQuery();
        }

        using (var insert = _connection.Command("""
                   INSERT INTO reminders (
                     id, kind, task_id, title, remind_at, repeat_rule, channel, status, fired_at, created_at, updated_at
                   ) VALUES (
                     @id, 'task_due', @taskId, @title, @remindAt, 'none', 'system', 'pending', NULL, @now, @now
                   )
                   """, transaction).Bind(
                   ("id", Ids.NewId()),
                   ("taskId", task.Id),
                   ("title", task.Title),
                   ("remindAt", desired),
                   ("now", now)))
        {
            insert.ExecuteNonQuery();
        }

        transaction.Commit();
        return true;
    }

    /// <summary>任务被删掉时清掉它的待触发提醒（fired 的保留，用作历史）。</summary>
    public int RemovePendingForTask(string taskId) => DeletePendingTaskDue(taskId);

    private IReadOnlyList<(string Id, string RemindAt)> ListPendingTaskDue(string taskId)
    {
        using var command = _connection.Command("""
            SELECT id, remind_at FROM reminders
            WHERE kind = 'task_due' AND task_id = @taskId AND status = 'pending'
            """).Bind(("taskId", taskId));

        return command.ReadAll(reader => (reader.Str("id"), reader.Str("remind_at")));
    }

    private int DeletePendingTaskDue(string taskId)
    {
        using var command = _connection.Command("""
            DELETE FROM reminders
            WHERE kind = 'task_due' AND task_id = @taskId AND status = 'pending'
            """).Bind(("taskId", taskId));
        return command.ExecuteNonQuery();
    }
}
