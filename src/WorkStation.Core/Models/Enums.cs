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

public enum ReportKind
{
    Weekly,
    Monthly
}
