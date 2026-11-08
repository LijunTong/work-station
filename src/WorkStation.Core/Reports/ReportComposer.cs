using System.Text;
using WorkStation.Core.Models;
using WorkStation.Core.Time;

namespace WorkStation.Core.Reports;

/// <summary>
/// 自动汇总：把一个周期的任务状态汇总成 Markdown。
///
/// 纯函数 —— 不碰数据库、不碰 UI，输入什么就产出什么，便于单测。
///
/// 三条口径：
/// <list type="bullet">
///   <item>已完成：status=Done 且完成时间落在周期内</item>
///   <item>未完成：还没做完（不含已取消）且截止时间落在周期内</item>
///   <item>逾期：未完成里，截止日早于今天的那些</item>
/// </list>
///
/// 「未完成」与「逾期」是包含关系（逾期 ⊂ 未完成），所以清单里分两小节写：
/// 「遗留未完成」放还没过期的，「逾期」放过期的 —— 两条不重复计入，数字与条数对得上。
/// </summary>
public static class ReportComposer
{
    /// <summary>周期统计数字。</summary>
    public static ReportStats ComputeStats(ReportPeriod period, IReadOnlyList<TaskItem> tasks, DateTime now)
    {
        var today = LocalTime.ToLocalDate(LocalTime.AsLocal(now));

        var completed = tasks.Count(t =>
            t.Status == TaskStatus.Done
            && InPeriod(CompletedDate(t), period));

        var carried = tasks.Where(t =>
            t.Status != TaskStatus.Done
            && t.Status != TaskStatus.Canceled
            && InPeriod(DueDate(t), period)).ToList();

        var overdue = carried.Count(t => string.CompareOrdinal(DueDate(t)!, today) < 0);

        return new ReportStats
        {
            Completed = completed,
            CarriedOver = carried.Count,
            Overdue = overdue
        };
    }

    /// <summary>
    /// 生成整篇自动汇总 Markdown。
    ///
    /// 换行一律写 "\n"：导出到别处的 .md 里混进 CRLF 会多出空行。
    /// </summary>
    public static string Compose(
        ReportPeriod period,
        IReadOnlyList<TaskItem> tasks,
        IReadOnlyList<Project> projects,
        DateTime now)
    {
        var today = LocalTime.ToLocalDate(LocalTime.AsLocal(now));
        var names = projects.ToDictionary(p => p.Id, p => p.Name);

        var completed = tasks
            .Where(t => t.Status == TaskStatus.Done && InPeriod(CompletedDate(t), period))
            .OrderBy(t => t.CompletedAt ?? string.Empty, StringComparer.Ordinal)
            .ThenBy(t => t.Title, StringComparer.Ordinal)
            .ToList();

        var carried = tasks
            .Where(t => t.Status != TaskStatus.Done
                        && t.Status != TaskStatus.Canceled
                        && InPeriod(DueDate(t), period))
            .OrderBy(t => t.DueAt ?? string.Empty, StringComparer.Ordinal)
            .ThenBy(t => t.Title, StringComparer.Ordinal)
            .ToList();

        var overdue = carried
            .Where(t => string.CompareOrdinal(DueDate(t)!, today) < 0)
            .ToList();

        var pending = carried
            .Where(t => string.CompareOrdinal(DueDate(t)!, today) >= 0)
            .ToList();

        var stats = new ReportStats
        {
            Completed = completed.Count,
            CarriedOver = carried.Count,
            Overdue = overdue.Count
        };

        var sb = new StringBuilder();
        sb.Append($"## {ReportPeriods.Label(period.Kind)} {ReportPeriods.Range(period)}\n\n");

        sb.Append("### 统计\n");
        sb.Append($"- 完成 {stats.Completed} 项 · 未完成 {stats.CarriedOver} 项（其中逾期 {stats.Overdue} 项）· 完成率 {stats.CompletionRate}%\n\n");

        sb.Append("### 已完成\n");
        AppendItems(sb, completed.Select(t => Line(t, names, null)));
        sb.Append("\n");

        sb.Append("### 遗留未完成\n");
        AppendItems(sb, pending.Select(t => Line(t, names, PendingLabel(t))));
        sb.Append("\n");

        sb.Append("### 逾期\n");
        AppendItems(sb, overdue.Select(t => Line(t, names, OverdueLabel(t, today))));

        return sb.ToString();
    }

    /// <summary>
    /// 最终稿 = 自动汇总 + 人工补充。任一为空就只取另一个。
    /// </summary>
    public static string Merge(string autoMarkdown, string manualText)
    {
        var auto = autoMarkdown.Trim();
        var manual = manualText.Trim();

        if (auto.Length == 0) return manual;
        if (manual.Length == 0) return auto;
        return $"{auto}\n\n{manual}";
    }

    private static void AppendItems(StringBuilder sb, IEnumerable<string> lines)
    {
        var any = false;
        foreach (var line in lines)
        {
            sb.Append(line).Append('\n');
            any = true;
        }

        if (!any) sb.Append("无\n");
    }

    /// <summary>一行任务：项目名（有才写）、标题、补充说明（截止日 / 逾期天数）。</summary>
    private static string Line(TaskItem task, IReadOnlyDictionary<string, string> names, string? suffix)
    {
        var prefix = task.ProjectId is not null && names.TryGetValue(task.ProjectId, out var name)
            ? $"[{name}] "
            : string.Empty;

        var tail = string.IsNullOrEmpty(suffix) ? string.Empty : $"（{suffix}）";
        return $"- {prefix}{task.Title}{tail}";
    }

    private static string OverdueLabel(TaskItem task, string today)
    {
        var due = DueDate(task);
        var days = (ParseDate(today) - ParseDate(due!)).Days;
        return $"截止 {ShortDate(task.DueAt)}，逾期 {days} 天";
    }

    /// <summary>遗留项的补充说明：截止日（只写 MM-DD）。</summary>
    private static string PendingLabel(TaskItem task)
    {
        var due = ShortDate(task.DueAt);
        return due.Length == 0 ? string.Empty : $"截止 {due}";
    }

    /// <summary>截止日只写 MM-DD：报告是给人看的，完整年月日在前面的标题里。</summary>
    private static string ShortDate(string? dueAt) =>
        string.IsNullOrEmpty(dueAt) || dueAt.Length < 10 ? string.Empty : dueAt.Substring(5, 5);

    private static string? CompletedDate(TaskItem task) => LocalTime.ToLocalDateOrNull(task.CompletedAt);

    private static string? DueDate(TaskItem task)
    {
        var due = task.DueAt;
        return string.IsNullOrEmpty(due) || due.Length < 10 ? null : due.Substring(0, 10);
    }

    private static bool InPeriod(string? date, ReportPeriod period) =>
        date is not null
        && string.CompareOrdinal(date, period.PeriodStart) >= 0
        && string.CompareOrdinal(date, period.PeriodEnd) <= 0;

    private static DateTime ParseDate(string value) =>
        DateTime.ParseExact(value, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
}
