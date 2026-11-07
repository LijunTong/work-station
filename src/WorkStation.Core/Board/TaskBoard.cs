using System.Globalization;
using WorkStation.Core.Models;
using WorkStation.Core.Time;

namespace WorkStation.Core.Board;

/// <summary>看板顶部概览条的时间口径。</summary>
public enum BoardTimeScope
{
    All,
    Today,
    Overdue,
    Week,
    Month
}

/// <summary>概览条上的四个数字。</summary>
public sealed record BoardOverview
{
    public int Today { get; init; }

    public int Overdue { get; init; }

    public int Blocked { get; init; }

    public int Week { get; init; }

    /// <summary>本月内到期、且尚未到点的任务数。</summary>
    public int Month { get; init; }
}

/// <summary>
/// 看板的分列与计数口径 —— 纯函数，放 Core 里可直接单测。
///
/// 三个容易写歪的地方，这里一次定死：
/// <list type="bullet">
///   <item>已取消的任务不进看板：四列只表达「待办 / 进行中 / 已阻塞 / 已完成」，取消是另一种语义</item>
///   <item>「已完成」列默认只看近 7 天，否则跑上几个月后这一列会长到无法翻阅</item>
///   <item>今日/逾期/本周直接复用 <see cref="TaskGrouping"/>，避免看板与任务页两套口径各算各的</item>
/// </list>
/// </summary>
public static class TaskBoard
{
    /// <summary>看板列顺序。与任务状态枚举顺序无关，改动会直接影响界面顺序。</summary>
    public static readonly IReadOnlyList<TaskStatus> ColumnOrder =
    [
        TaskStatus.Todo,
        TaskStatus.Doing,
        TaskStatus.Blocked,
        TaskStatus.Done
    ];

    public const int DoneWindowDays = 7;

    public static bool IsOpen(TaskItem task) =>
        task.Status is not (TaskStatus.Done or TaskStatus.Canceled);

    public static string StatusTitle(TaskStatus status) => status switch
    {
        TaskStatus.Todo => "待办",
        TaskStatus.Doing => "进行中",
        TaskStatus.Blocked => "已阻塞",
        TaskStatus.Done => "已完成",
        _ => "已取消"
    };

    /// <summary>
    /// 是否落在「已完成」列的展示窗口内。
    ///
    /// 时间戳是 ISO UTC，比较前必须换算到本地；解析不了的老数据一律保留 ——
    /// 宁可多显示一条，也不要让用户以为任务丢了。
    /// </summary>
    public static bool IsInDoneWindow(TaskItem task, DateTime now, int days = DoneWindowDays)
    {
        if (task.Status != TaskStatus.Done) return false;

        var stamp = ParseTimestamp(task.CompletedAt) ?? ParseTimestamp(task.UpdatedAt);
        if (stamp is null) return true;

        return stamp.Value >= LocalTime.AsLocal(now).AddDays(-days);
    }

    /// <summary>
    /// 时间筛选是否命中。
    ///
    /// 已完成任务不受时间筛选影响：它们的截止时间早已过去，跟着筛会被整列滤空，
    /// 而用户此刻想看的正是「今天做了哪些事」。
    /// </summary>
    public static bool MatchesTimeScope(TaskItem task, BoardTimeScope scope, DateTime now)
    {
        if (scope == BoardTimeScope.All) return true;
        if (task.Status == TaskStatus.Done) return true;
        if (!IsOpen(task)) return false;

        // 「本月」不是 TaskGrouping 里的分组（任务页的分组是「以后」，不是「本月」），
        // 这里单独判定，口径与概览条的计数一致。
        if (scope == BoardTimeScope.Month) return IsDueWithinMonth(task, now);

        var target = scope switch
        {
            BoardTimeScope.Today => Core.TaskGroup.Today,
            BoardTimeScope.Overdue => Core.TaskGroup.Overdue,
            _ => Core.TaskGroup.Week
        };

        return TaskGrouping.Group(task, now) == target;
    }

    /// <summary>
    /// 到期日在本月内、且尚未到点。
    ///
    /// 与「本周」同一套口径：只算还没到点的那些 —— 逾期的事已经在「逾期」里，
    /// 再在「本月」里数一遍会让两个数字互相打架。
    /// </summary>
    public static bool IsDueWithinMonth(TaskItem task, DateTime now)
    {
        var due = task.DueAt;
        if (string.IsNullOrEmpty(due)) return false;

        var local = LocalTime.AsLocal(now);
        // 已到点的不算「本月还没到期」
        if (string.CompareOrdinal(due, LocalTime.ToLocalDateTime(local)) < 0) return false;

        var month = LocalTime.ToLocalDate(local).Substring(0, 7);
        return due.Length >= 10 && due.Substring(0, 10).StartsWith(month, StringComparison.Ordinal);
    }

    /// <summary>概览条计数。口径与 <see cref="MatchesTimeScope"/> 一致，否则「点进去数量对不上」。</summary>
    public static BoardOverview ComputeOverview(IReadOnlyList<TaskItem> tasks, DateTime now)
    {
        var open = tasks.Where(IsOpen).ToList();

        return new BoardOverview
        {
            Today = open.Count(t => TaskGrouping.Group(t, now) == Core.TaskGroup.Today),
            Overdue = open.Count(t => TaskGrouping.Group(t, now) == Core.TaskGroup.Overdue),
            Blocked = tasks.Count(t => t.Status == TaskStatus.Blocked),
            Week = open.Count(t => TaskGrouping.Group(t, now) == Core.TaskGroup.Week),
            Month = open.Count(t => IsDueWithinMonth(t, now))
        };
    }

    private static DateTime? ParseTimestamp(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;

        if (!DateTime.TryParse(
                value.Trim(),
                CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind,
                out var parsed))
        {
            return null;
        }

        return LocalTime.AsLocal(parsed);
    }
}
