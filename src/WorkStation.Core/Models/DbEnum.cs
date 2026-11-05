namespace WorkStation.Core.Models;

/// <summary>
/// 枚举 ↔ 数据库字符串的显式映射。
/// 不用反射 + Description 特性的原因：那套在热路径上每次都要查特性表，
/// 而这里每个方向都是一次 switch，写死最省心也最不容易出错。
/// </summary>
public static class DbEnum
{
    public static string ToDb(this ProjectStatus value) => value switch
    {
        ProjectStatus.Planning => "planning",
        ProjectStatus.Active => "active",
        ProjectStatus.Paused => "paused",
        ProjectStatus.Done => "done",
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, null)
    };

    public static ProjectStatus ParseProjectStatus(string value) => value switch
    {
        "planning" => ProjectStatus.Planning,
        "active" => ProjectStatus.Active,
        "paused" => ProjectStatus.Paused,
        "done" => ProjectStatus.Done,
        _ => throw new ArgumentException($"未知的项目状态：{value}", nameof(value))
    };

    public static string ToDb(this TaskStatus value) => value switch
    {
        TaskStatus.Todo => "todo",
        TaskStatus.Doing => "doing",
        TaskStatus.Blocked => "blocked",
        TaskStatus.Done => "done",
        TaskStatus.Canceled => "canceled",
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, null)
    };

    public static TaskStatus ParseTaskStatus(string value) => value switch
    {
        "todo" => TaskStatus.Todo,
        "doing" => TaskStatus.Doing,
        "blocked" => TaskStatus.Blocked,
        "done" => TaskStatus.Done,
        "canceled" => TaskStatus.Canceled,
        _ => throw new ArgumentException($"未知的任务状态：{value}", nameof(value))
    };

    public static string ToDb(this Priority value) => value switch
    {
        Priority.High => "high",
        Priority.Medium => "medium",
        Priority.Low => "low",
        Priority.None => "none",
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, null)
    };

    public static Priority ParsePriority(string value) => value switch
    {
        "high" => Priority.High,
        "medium" => Priority.Medium,
        "low" => Priority.Low,
        "none" => Priority.None,
        _ => throw new ArgumentException($"未知的优先级：{value}", nameof(value))
    };

    public static string ToDb(this ProgressMode value) => value switch
    {
        ProgressMode.Auto => "auto",
        ProgressMode.Manual => "manual",
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, null)
    };

    public static ProgressMode ParseProgressMode(string value) => value switch
    {
        "auto" => ProgressMode.Auto,
        "manual" => ProgressMode.Manual,
        _ => throw new ArgumentException($"未知的进度模式：{value}", nameof(value))
    };

    public static string ToDb(this ReminderKind value) => value switch
    {
        ReminderKind.TaskDue => "task_due",
        ReminderKind.Custom => "custom",
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, null)
    };

    public static ReminderKind ParseReminderKind(string value) => value switch
    {
        "task_due" => ReminderKind.TaskDue,
        "custom" => ReminderKind.Custom,
        _ => throw new ArgumentException($"未知的提醒类型：{value}", nameof(value))
    };

    public static string ToDb(this ReminderStatus value) => value switch
    {
        ReminderStatus.Pending => "pending",
        ReminderStatus.Fired => "fired",
        ReminderStatus.Dismissed => "dismissed",
        ReminderStatus.Failed => "failed",
        ReminderStatus.Done => "done",
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, null)
    };

    public static ReminderStatus ParseReminderStatus(string value) => value switch
    {
        "pending" => ReminderStatus.Pending,
        "fired" => ReminderStatus.Fired,
        "dismissed" => ReminderStatus.Dismissed,
        "failed" => ReminderStatus.Failed,
        "done" => ReminderStatus.Done,
        _ => throw new ArgumentException($"未知的提醒状态：{value}", nameof(value))
    };

    public static string ToDb(this RepeatRule value) => value switch
    {
        RepeatRule.None => "none",
        RepeatRule.Daily => "daily",
        RepeatRule.Weekly => "weekly",
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, null)
    };

    public static RepeatRule ParseRepeatRule(string value) => value switch
    {
        "none" => RepeatRule.None,
        "daily" => RepeatRule.Daily,
        "weekly" => RepeatRule.Weekly,
        _ => throw new ArgumentException($"未知的重复规则：{value}", nameof(value))
    };

    public static string ToDb(this ReportKind value) => value switch
    {
        ReportKind.Weekly => "weekly",
        ReportKind.Monthly => "monthly",
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, null)
    };

    public static ReportKind ParseReportKind(string value) => value switch
    {
        "weekly" => ReportKind.Weekly,
        "monthly" => ReportKind.Monthly,
        _ => throw new ArgumentException($"未知的报告类型：{value}", nameof(value))
    };
}
