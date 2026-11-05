namespace WorkStation.Core.Models;

public sealed record Report
{
    public required string Id { get; init; }

    public ReportKind Kind { get; init; }

    /// <summary>本地日历日 YYYY-MM-DD。</summary>
    public required string PeriodStart { get; init; }

    /// <summary>本地日历日 YYYY-MM-DD。</summary>
    public required string PeriodEnd { get; init; }

    public string AutoMarkdown { get; init; } = string.Empty;

    public string ManualText { get; init; } = string.Empty;

    public string FinalMarkdown { get; init; } = string.Empty;

    public required string CreatedAt { get; init; }

    public required string UpdatedAt { get; init; }
}
