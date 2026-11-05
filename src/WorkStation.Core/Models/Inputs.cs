namespace WorkStation.Core.Models;

public sealed record CreateProjectInput
{
    public required string Name { get; init; }

    public string? Description { get; init; }

    public ProjectStatus? Status { get; init; }

    public Priority? Priority { get; init; }

    public string? Color { get; init; }

    public ProgressMode? ProgressMode { get; init; }

    public int? ProgressManual { get; init; }

    public string? StartDate { get; init; }

    public string? DueDate { get; init; }
}

public sealed record UpdateProjectInput
{
    public Optional<string> Name { get; init; } = Optional<string>.None;

    public Optional<string?> Description { get; init; } = Optional<string?>.None;

    public Optional<ProjectStatus?> Status { get; init; } = Optional<ProjectStatus?>.None;

    public Optional<Priority?> Priority { get; init; } = Optional<Priority?>.None;

    public Optional<string?> Color { get; init; } = Optional<string?>.None;

    public Optional<ProgressMode?> ProgressMode { get; init; } = Optional<ProgressMode?>.None;

    public Optional<int?> ProgressManual { get; init; } = Optional<int?>.None;

    public Optional<string?> StartDate { get; init; } = Optional<string?>.None;

    public Optional<string?> DueDate { get; init; } = Optional<string?>.None;

    public Optional<int?> SortOrder { get; init; } = Optional<int?>.None;

    public Optional<string?> ArchivedAt { get; init; } = Optional<string?>.None;
}

public sealed record CreateTaskInput
{
    public required string Title { get; init; }

    public string? ProjectId { get; init; }

    public string? Notes { get; init; }

    public TaskStatus? Status { get; init; }

    public Priority? Priority { get; init; }

    /// <summary>本地墙钟 YYYY-MM-DDTHH:mm，写入前会校验格式。</summary>
    public string? DueAt { get; init; }

    public int? RemindBefore { get; init; }

    public double? Estimate { get; init; }

    public double? Spent { get; init; }

    public string? BlockedReason { get; init; }
}

public sealed record UpdateTaskInput
{
    public Optional<string> Title { get; init; } = Optional<string>.None;

    public Optional<string?> ProjectId { get; init; } = Optional<string?>.None;

    public Optional<string?> Notes { get; init; } = Optional<string?>.None;

    public Optional<TaskStatus?> Status { get; init; } = Optional<TaskStatus?>.None;

    public Optional<Priority?> Priority { get; init; } = Optional<Priority?>.None;

    /// <summary>本地墙钟 YYYY-MM-DDTHH:mm，写入前会校验格式。</summary>
    public Optional<string?> DueAt { get; init; } = Optional<string?>.None;

    public Optional<int?> RemindBefore { get; init; } = Optional<int?>.None;

    public Optional<double?> Estimate { get; init; } = Optional<double?>.None;

    public Optional<double?> Spent { get; init; } = Optional<double?>.None;

    public Optional<string?> BlockedReason { get; init; } = Optional<string?>.None;

    public Optional<int?> SortOrder { get; init; } = Optional<int?>.None;
}

/// <summary>
/// 任务列表过滤条件。语义与 Electron 版 TaskFilter 一致，但把歧义处写死：
/// <list type="bullet">
///   <item>ProjectId：None = 不按项目过滤，Some(null) = 只看独立任务，Some(id) = 只看该项目的</item>
///   <item>Status / Priority / TagIds：null 或空集合 = 不过滤</item>
///   <item>IncludeDone：null = 不额外限制；false = 排除 done 与 canceled</item>
///   <item>status 与 includeDone 同时给定时，以 status 为准（includeDone 被忽略）</item>
/// </list>
/// </summary>
public sealed record TaskFilter
{
    public Optional<string?> ProjectId { get; init; } = Optional<string?>.None;

    public IReadOnlyList<TaskStatus>? Status { get; init; }

    public IReadOnlyList<Priority>? Priority { get; init; }

    public IReadOnlyList<string>? TagIds { get; init; }

    /// <summary>本地墙钟 YYYY-MM-DDTHH:mm，due_at 严格小于它。</summary>
    public string? DueBefore { get; init; }

    /// <summary>本地墙钟 YYYY-MM-DDTHH:mm，due_at 大于等于它。</summary>
    public string? DueAfter { get; init; }

    public bool? IncludeDone { get; init; }

    /// <summary>标题与备注的模糊搜索关键字。</summary>
    public string? Search { get; init; }
}

/// <summary>项目卡片上的任务统计。</summary>
public sealed record TaskCounts
{
    public int Total { get; init; }

    public int Done { get; init; }

    public int Doing { get; init; }

    public int Overdue { get; init; }
}
