namespace WorkStation.Core.Models;

/// <summary>
/// 一类报告提醒的设置。
///
/// 三类提醒共用一个形状：日报不用星期与几号，周报不用几号，月报不用星期 ——
/// 拆成三个类型反而让设置页与存取都变复杂。
/// </summary>
public sealed record ReportReminderEntry
{
    public bool Enabled { get; init; }

    /// <summary>本地墙钟 HH:mm，24 小时制。</summary>
    public string Time { get; init; } = "09:00";

    /// <summary>周报用：ISO 星期几，1=周一 … 7=周日。</summary>
    public int DayOfWeek { get; init; } = 5;

    /// <summary>月报用：几号，1–31。没有该号的月份落到当月最后一天。</summary>
    public int DayOfMonth { get; init; } = 31;
}

/// <summary>
/// 三类报告提醒的设置（存在 settings.report_reminders）。
/// </summary>
public sealed record ReportReminderSettings
{
    /// <summary>日报：每天一次，默认 09:00。</summary>
    public ReportReminderEntry Daily { get; init; } = new() { Enabled = true, Time = "09:00" };

    /// <summary>周报：每周一次，默认周五 17:00。</summary>
    public ReportReminderEntry Weekly { get; init; } = new() { Enabled = true, Time = "17:00", DayOfWeek = 5 };

    /// <summary>月报：每月一次，默认每月 31 号 17:00（没有 31 号的月落到当月最后一天）。</summary>
    public ReportReminderEntry Monthly { get; init; } = new() { Enabled = true, Time = "17:00", DayOfMonth = 31 };
}
