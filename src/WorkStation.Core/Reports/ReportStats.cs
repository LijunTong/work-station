namespace WorkStation.Core.Reports;

/// <summary>
/// 一个周期的统计数字。
///
/// 口径与仪表盘**刻意不同**：仪表盘答「要扛多少活」，报告答「这段时间干了什么、还欠什么」。
/// </summary>
public sealed record ReportStats
{
    /// <summary>周期内完成的任务数（按完成时间落窗）。</summary>
    public int Completed { get; init; }

    /// <summary>
    /// 周期内到期但还没完成的任务数 —— **含已逾期的那部分**。
    /// </summary>
    public int CarriedOver { get; init; }

    /// <summary><see cref="CarriedOver"/> 里截至今天已经过期的部分。</summary>
    public int Overdue { get; init; }

    /// <summary>周期内到期（或完成）的任务总数，完成率的分母。</summary>
    public int Total => Completed + CarriedOver;

    /// <summary>完成率 0–100。周期内没有到期任务时为 0。</summary>
    public int CompletionRate => Total == 0
        ? 0
        : (int)Math.Round(Completed * 100.0 / Total);
}
