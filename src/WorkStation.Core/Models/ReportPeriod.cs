namespace WorkStation.Core.Models;

/// <summary>
/// 一个报告周期。<see cref="PeriodStart"/> 与 <see cref="PeriodEnd"/> 都是本地日历日
/// YYYY-MM-DD。日报起止同一天。
/// </summary>
public sealed record ReportPeriod
{
    public required ReportKind Kind { get; init; }

    /// <summary>本地日历日 YYYY-MM-DD。</summary>
    public required string PeriodStart { get; init; }

    /// <summary>本地日历日 YYYY-MM-DD。</summary>
    public required string PeriodEnd { get; init; }
}
