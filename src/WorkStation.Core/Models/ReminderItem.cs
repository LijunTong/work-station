namespace WorkStation.Core.Models;

/// <summary>
/// 提醒的展示态：<see cref="Reminder"/> 加上它指向的任务与项目信息。
///
/// 任务类提醒的标题与项目名不落库、而是在查询时 join 出来 —— 改了任务标题，
/// 提醒列表跟着变，不会留一条「旧标题」的僵尸提醒。
/// </summary>
public sealed record ReminderItem
{
    public required string Id { get; init; }

    public ReminderKind Kind { get; init; }

    public string? TaskId { get; init; }

    /// <summary>展示标题：任务类取任务当前标题，自定义提醒取自身标题。</summary>
    public required string Title { get; init; }

    /// <summary>本地墙钟 YYYY-MM-DDTHH:mm。</summary>
    public required string RemindAt { get; init; }

    public RepeatRule RepeatRule { get; init; }

    public ReminderStatus Status { get; init; }

    public string? FiredAt { get; init; }

    public string? ProjectName { get; init; }

    /// <summary>任务自身的截止时间，用于通知文案里的「截止 11-05 18:00」。</summary>
    public string? TaskDueAt { get; init; }

    /// <summary>任务当前状态。属性名不叫 TaskStatus：会遮住同名的枚举类型。</summary>
    public TaskStatus? TaskState { get; init; }

    /// <summary>任务已被删掉（外键级联后不该出现，兜底防御）。</summary>
    public bool IsOrphaned => Kind == ReminderKind.TaskDue && TaskState is null;

    /// <summary>任务已完成/取消：提醒该收起来了。</summary>
    public bool IsTaskClosed => TaskState is TaskStatus.Done or TaskStatus.Canceled;
}

/// <summary>提醒数量：<see cref="Unhandled"/> 是已弹出但还没处理的，驱动侧边栏徽标。</summary>
public sealed record ReminderCounts
{
    public int Unhandled { get; init; }

    public int Upcoming { get; init; }
}

/// <summary>提醒列表的三种视图。</summary>
public enum ReminderScope
{
    /// <summary>还没到点（pending）。</summary>
    Upcoming,

    /// <summary>已弹出、待处理（fired）。</summary>
    Triggered,

    /// <summary>已处理 / 已忽略 / 发送失败，按最近更新取前 20 条。</summary>
    Handled
}

/// <summary>新建一条自定义提醒（任务截止提醒由保存任务时自动同步，不走这里）。</summary>
public sealed record CreateReminderInput
{
    public required string Title { get; init; }

    /// <summary>本地墙钟 YYYY-MM-DDTHH:mm。</summary>
    public required string RemindAt { get; init; }

    public RepeatRule RepeatRule { get; init; } = RepeatRule.None;
}

/// <summary>每日站会提醒的设置（存在 settings.daily_review）。</summary>
public sealed record DailyReviewSettings
{
    public bool Enabled { get; init; } = true;

    /// <summary>HH:mm，24 小时制。</summary>
    public string Time { get; init; } = "09:00";
}
