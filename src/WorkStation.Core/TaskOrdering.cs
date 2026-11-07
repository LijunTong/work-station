using WorkStation.Core.Models;
using WorkStation.Core.Time;

namespace WorkStation.Core;

/// <summary>
/// 任务列表的统一排序口径 —— 仪表盘清单、任务页分组、看板分列都走这里，
/// 免得三处各排各的，同一批任务在三个页面顺序不一样。
///
/// 未完成任务的排序键：**优先级 → 逾期 → 截止时间 → 创建时间**。
/// 优先级排第一是用户显式要求的：谁最重要谁先做。
/// 逾期与截止时间是同优先级内部的兜底 —— 不至于把「今天必须交」的那条压在后面。
///
/// 已完成的任务不套这套：那一列是历史流水，按完成时间倒序才读得通
/// （刚做完的事排在最上面），优先级对做完的事已经没有意义。
/// </summary>
public static class TaskOrdering
{
    /// <summary>
    /// 优先级权重：数值越小越靠前（高 → 中 → 低 → 无）。
    ///
    /// **别直接拿枚举值排序**：Priority 的枚举值是 High=0 / Medium=1 / Low=2 / None=3，
    /// 看着像「数字大优先级高」，其实正好相反 —— 用 OrderByDescending 会把
    /// 「无优先级」排到最前面，把高优先级压到最后。这里给一个显式权重，
    /// 免得各处再手写一遍方向不同的 OrderBy。
    /// </summary>
    public static int PriorityWeight(Priority priority) => priority switch
    {
        Priority.High => 0,
        Priority.Medium => 1,
        Priority.Low => 2,
        _ => 3
    };

    /// <summary>
    /// 未完成任务的排序比较器：优先级 → 逾期 → 截止时间 → 创建时间。
    /// </summary>
    /// <param name="now">当前时间，用来判定逾期（按墙钟比，不是按日历日）。</param>
    public static IComparer<TaskItem> OpenTaskComparer(DateTime now)
    {
        var nowLocal = LocalTime.ToLocalDateTime(LocalTime.AsLocal(now));

        return Comparer<TaskItem>.Create((a, b) =>
        {
            var byPriority = PriorityWeight(a.Priority).CompareTo(PriorityWeight(b.Priority));
            if (byPriority != 0) return byPriority;

            var byOverdue = OverdueRank(a.DueAt, nowLocal).CompareTo(OverdueRank(b.DueAt, nowLocal));
            if (byOverdue != 0) return byOverdue;

            var byDue = CompareDue(a.DueAt, b.DueAt);
            if (byDue != 0) return byDue;

            return string.CompareOrdinal(a.CreatedAt, b.CreatedAt);
        });
    }

    /// <summary>
    /// 已完成任务的排序比较器：完成时间倒序（刚做完的排在最上面）。
    /// 没有完成时间的退回用更新时间。
    /// </summary>
    public static IComparer<TaskItem> CompletedTaskComparer() =>
        Comparer<TaskItem>.Create((a, b) =>
            string.CompareOrdinal(b.CompletedAt ?? b.UpdatedAt, a.CompletedAt ?? a.UpdatedAt));

    /// <summary>逾期的排前面（0 = 逾期，1 = 没逾期）。没截止时间的算「没逾期」。</summary>
    private static int OverdueRank(string? dueAt, string nowLocalDateTime) =>
        Formatting.IsOverdue(dueAt, nowLocalDateTime) ? 0 : 1;

    /// <summary>截止时间早的在前；没有截止时间的沉到最后。</summary>
    private static int CompareDue(string? a, string? b)
    {
        var aEmpty = string.IsNullOrEmpty(a);
        var bEmpty = string.IsNullOrEmpty(b);
        if (aEmpty != bEmpty) return aEmpty ? 1 : -1;
        if (aEmpty) return 0;

        return string.CompareOrdinal(a, b);
    }
}
