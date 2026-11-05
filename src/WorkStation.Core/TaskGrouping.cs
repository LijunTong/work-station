using WorkStation.Core.Models;
using WorkStation.Core.Time;

namespace WorkStation.Core;

/// <summary>任务在列表里的分组。</summary>
public enum TaskGroup
{
    Overdue,
    Today,
    Week,
    Later,
    Someday
}

/// <summary>
/// 任务分组 —— 纯函数，放在 Core 里以便直接单测（Electron 版把 groupOf 塞在页面组件里，
/// 导致「分组/页面零测试」，Task 12 的周日边界缺陷就是这样漏过去的）。
///
/// 关键口径：**逾期用墙钟判定，不是日历日**。
/// 「今天 09:00 到期、现在 10:00」的任务必须落进逾期组，
/// 否则同一个任务会在列表标红却不计入逾期分组计数，与项目徽标、概览条口径打架。
/// </summary>
public static class TaskGrouping
{
    public static TaskGroup Group(TaskItem task, DateTime now)
    {
        if (string.IsNullOrEmpty(task.DueAt)) return TaskGroup.Someday;

        var dueAt = task.DueAt;
        if (!LocalTime.IsLocalDateTime(dueAt))
        {
            throw new ArgumentException(
                $"dueAt 必须是本地墙钟格式 YYYY-MM-DDTHH:mm，收到：{dueAt}",
                nameof(task));
        }

        var nowDateTime = LocalTime.ToLocalDateTime(now);

        // 到期时刻已过即为逾期（严格小于：恰好等于当前时刻不算逾期）
        if (string.CompareOrdinal(dueAt, nowDateTime) < 0) return TaskGroup.Overdue;

        var dueDate = dueAt.Substring(0, 10);
        var nowDate = nowDateTime.Substring(0, 10);
        if (string.CompareOrdinal(dueDate, nowDate) == 0) return TaskGroup.Today;

        // 本周以周一为首日、结束于周日
        if (string.CompareOrdinal(dueDate, WeekEndDate(now)) <= 0) return TaskGroup.Week;

        return TaskGroup.Later;
    }

    /// <summary>本周最后一天（周日）的本地日历日。</summary>
    public static string WeekEndDate(DateTime now)
    {
        var local = LocalTime.AsLocal(now);
        var daysUntilSunday = local.DayOfWeek == DayOfWeek.Sunday ? 0 : 7 - (int)local.DayOfWeek;
        return LocalTime.ToLocalDate(local.AddDays(daysUntilSunday));
    }
}
