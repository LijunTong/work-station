using System.Globalization;
using WorkStation.Core.Models;
using WorkStation.Core.Time;

namespace WorkStation.Core.Reminders;

/// <summary>
/// 提醒的纯计算部分：提前量换算、重复规则的下一次、到点判定。
/// 不碰数据库、不碰时钟 —— 时间一律由调用方传入，跨日/跨周边界才能被测试真正覆盖。
/// </summary>
public static class ReminderPlanner
{
    /// <summary>提前量上限 30 天：再大就不是「提前提醒」而是日历了。</summary>
    public const int MaxLeadMinutes = 60 * 24 * 30;

    public static bool IsValidLead(int minutes) => minutes >= 0 && minutes <= MaxLeadMinutes;

    /// <summary>
    /// 任务提醒的触发时刻 = 截止时刻 − 提前量。
    /// 提前量为 0 视为「不提醒」：默认新建的任务都带 0，若按「准时提醒」处理，
    /// 每个任务到点都会弹一次，提醒立刻失去意义。
    /// </summary>
    public static string? ComputeTaskRemindAt(string? dueAt, int leadMinutes)
    {
        if (string.IsNullOrWhiteSpace(dueAt) || leadMinutes <= 0) return null;
        if (!LocalTime.IsLocalDateTime(dueAt)) return null;
        if (!DateTime.TryParseExact(dueAt, "yyyy-MM-dd'T'HH:mm", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var due)) return null;

        return LocalTime.ToLocalDateTime(due.AddMinutes(-leadMinutes));
    }

    /// <summary>
    /// 重复提醒的下一次触发时刻：严格晚于 <paramref name="now"/>。
    /// 直接按「相差几个周期」算，不做逐次递推 —— 应用关了三周再打开时，
    /// 逐次递推要把同一个循环跑 21 遍才能落到今天。
    /// </summary>
    public static string? NextOccurrence(string remindAt, RepeatRule rule, string now)
    {
        if (rule == RepeatRule.None) return null;

        if (!DateTime.TryParseExact(remindAt, "yyyy-MM-dd'T'HH:mm", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var at)) return null;
        if (!DateTime.TryParseExact(now, "yyyy-MM-dd'T'HH:mm", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var current)) return null;

        if (at > current) return LocalTime.ToLocalDateTime(at);

        var periodDays = rule == RepeatRule.Daily ? 1 : 7;
        var steps = (long)Math.Floor((current - at).TotalDays / periodDays) + 1;
        var next = at.AddDays(steps * periodDays);

        // 浮点除法在整周期边界上可能差一跳，落到过去就再补一个周期
        while (next <= current) next = next.AddDays(periodDays);

        return LocalTime.ToLocalDateTime(next);
    }

    /// <summary>到点判定：本地墙钟定长字符串，字典序即时间序。</summary>
    public static bool IsDue(string remindAt, string now) => string.CompareOrdinal(remindAt, now) <= 0;

    public static string DescribeRepeat(RepeatRule rule) => rule switch
    {
        RepeatRule.Daily => "每天",
        RepeatRule.Weekly => "每周",
        _ => "不重复"
    };
}
