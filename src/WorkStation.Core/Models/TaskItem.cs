namespace WorkStation.Core.Models;

/// <summary>
/// 任务实体。
///
/// 为什么不叫 Task：C# 里 <c>System.Threading.Tasks.Task</c> 是语言自带的，
/// 实体若也叫 Task，仓储文件同时隐式 using System.Threading.Tasks 与
/// using WorkStation.Core.Models 之后，引用 Task 会变成 CS0104 二义性错误。
/// 改名 TaskItem 后两边都干净。
/// </summary>
public sealed record TaskItem
{
    public required string Id { get; init; }

    /// <summary>空 = 独立任务（不属于任何项目）。</summary>
    public string? ProjectId { get; init; }

    public required string Title { get; init; }

    public string Notes { get; init; } = string.Empty;

    public TaskStatus Status { get; init; } = TaskStatus.Todo;

    public Priority Priority { get; init; } = Priority.None;

    /// <summary>本地墙钟 YYYY-MM-DDTHH:mm，不是日期串也不是 ISO UTC 串。</summary>
    public string? DueAt { get; init; }

    /// <summary>截止前多少分钟提醒（分钟）。</summary>
    public int RemindBefore { get; init; }

    /// <summary>预估工时（小时）。</summary>
    public double? Estimate { get; init; }

    /// <summary>实际工时（小时）。</summary>
    public double? Spent { get; init; }

    /// <summary>status='blocked' 时的阻塞原因。</summary>
    public string? BlockedReason { get; init; }

    public int SortOrder { get; init; }

    /// <summary>ISO 8601 UTC 时间戳。</summary>
    public required string CreatedAt { get; init; }

    /// <summary>ISO 8601 UTC 时间戳。</summary>
    public required string UpdatedAt { get; init; }

    /// <summary>首次进入 doing 的时间（ISO 8601 UTC）。</summary>
    public string? StartedAt { get; init; }

    /// <summary>进入 done 的时间（ISO 8601 UTC）。</summary>
    public string? CompletedAt { get; init; }
}
