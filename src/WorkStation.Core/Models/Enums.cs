namespace WorkStation.Core.Models;

// 枚举成员用 PascalCase，数据库里存的是小写字符串，转换在 DbEnum 里做。
// 字符串值与 Electron 版 src/types/entities.ts 逐字一致，不可擅改。
public enum ProjectStatus
{
    Planning,
    Active,
    Paused,
    Done
}

public enum TaskStatus
{
    Todo,
    Doing,
    Blocked,
    Done,
    Canceled
}

public enum Priority
{
    High,
    Medium,
    Low,
    None
}

public enum ProgressMode
{
    Auto,
    Manual
}

public enum ReminderKind
{
    TaskDue,
    Custom
}

public enum ReminderStatus
{
    Pending,
    Fired,
    Dismissed,
    Failed,
    Done
}

public enum RepeatRule
{
    None,
    Daily,
    Weekly
}

/// <summary>
/// 报告类型。数据库里存的是小写字符串（见 DbEnum）。
///
/// 说明：Electron 版只有 weekly / monthly，本项目新增 daily ——
/// 迁移 v3 重建了 reports 表并放开 CHECK 约束，老库照常能升。
/// </summary>
public enum ReportKind
{
    Daily,
    Weekly,
    Monthly
}
