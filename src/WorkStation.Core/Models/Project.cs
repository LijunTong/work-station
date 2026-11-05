namespace WorkStation.Core.Models;

public sealed record Project
{
    public required string Id { get; init; }

    public required string Name { get; init; }

    public string Description { get; init; } = string.Empty;

    public ProjectStatus Status { get; init; } = ProjectStatus.Planning;

    public Priority Priority { get; init; } = Priority.None;

    public string? Color { get; init; }

    public ProgressMode ProgressMode { get; init; } = ProgressMode.Auto;

    public int? ProgressManual { get; init; }

    /// <summary>本地日历日 YYYY-MM-DD。</summary>
    public string? StartDate { get; init; }

    /// <summary>本地日历日 YYYY-MM-DD。</summary>
    public string? DueDate { get; init; }

    public int SortOrder { get; init; }

    /// <summary>ISO 8601 UTC 时间戳。</summary>
    public required string CreatedAt { get; init; }

    /// <summary>ISO 8601 UTC 时间戳。</summary>
    public required string UpdatedAt { get; init; }

    /// <summary>非空表示已归档（软删除）。</summary>
    public string? ArchivedAt { get; init; }
}
