using System.Globalization;
using WorkStation.Core.Models;

namespace WorkStation.Core.Reminders;

/// <summary>
/// 每日站会提醒：不落 reminders 表（每天一次、没有「处理」的历史意义），
/// 由调度器按设置的时间合成一次。这里只放「今天该不该发」「今天发过没有」的判定与文案。
/// </summary>
public static class DailyReviewPlanner
{
    public const string DefaultTime = "09:00";

    public static bool IsValidTime(string? value) =>
        value is not null
        && TimeSpan.TryParseExact(value, @"hh\:mm", CultureInfo.InvariantCulture, out _);

    /// <summary>
    /// 今天这一场是否已经到点。<paramref name="lastFiredDate"/> 是上次实际发出的日期
    /// （YYYY-MM-DD），等于今天就说明已经发过，不再重复。
    /// </summary>
    public static bool ShouldFire(DailyReviewSettings settings, string nowLocal, string? lastFiredDate)
    {
        if (!settings.Enabled) return false;

        var time = IsValidTime(settings.Time) ? settings.Time : DefaultTime;
        if (!DateTime.TryParseExact(nowLocal, "yyyy-MM-dd'T'HH:mm", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var now)) return false;

        if (!TimeSpan.TryParseExact(time, @"hh\:mm", CultureInfo.InvariantCulture, out var at)) return false;

        var today = now.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        if (string.Equals(lastFiredDate, today, StringComparison.Ordinal)) return false;

        return now.TimeOfDay >= at;
    }

    /// <summary>昨日完成 / 逾期 / 今晚到期。三个数字都是调度时刻实时查库得到的。</summary>
    public static string BuildMessage(int yesterdayDone, int overdue, int dueToday) =>
        $"昨日完成 {yesterdayDone} 项 · 逾期 {overdue} 项 · 今晚到期 {dueToday} 项";

    /// <summary>
    /// 站会文案里的三个数字。口径与任务页分组一致：
    /// 已完成/已取消都不算逾期与到期；「昨天完成」按 completed_at 的本地日期算。
    /// </summary>
    public static DailyReviewCounts ComputeCounts(IReadOnlyList<TaskItem> tasks, string nowLocal)
    {
        if (!DateTime.TryParseExact(nowLocal, "yyyy-MM-dd'T'HH:mm", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var now))
        {
            return new DailyReviewCounts();
        }

        var yesterday = now.Date.AddDays(-1);
        var today = now.Date;
        var todayKey = today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        var done = 0;
        var overdue = 0;
        var dueToday = 0;

        foreach (var task in tasks)
        {
            if (task.Status == TaskStatus.Done)
            {
                // completed_at 是 ISO UTC 时间戳，按本地日期落到「昨天」才算
                if (task.CompletedAt is not { } completedAt) continue;
                if (!DateTime.TryParse(completedAt, CultureInfo.InvariantCulture,
                        DateTimeStyles.RoundtripKind, out var completed)) continue;
                if (completed.ToLocalTime().Date == yesterday) done++;
                continue;
            }

            if (task.Status == TaskStatus.Canceled) continue;
            if (task.DueAt is not { Length: >= 16 } due) continue;

            if (string.CompareOrdinal(due, nowLocal) < 0) overdue++;
            else if (due.StartsWith(todayKey, StringComparison.Ordinal)) dueToday++;
        }

        return new DailyReviewCounts { YesterdayDone = done, Overdue = overdue, DueToday = dueToday };
    }
}

/// <summary>每日站会提醒里的三个数字。</summary>
public sealed record DailyReviewCounts
{
    public int YesterdayDone { get; init; }

    public int Overdue { get; init; }

    public int DueToday { get; init; }

    public string Message => DailyReviewPlanner.BuildMessage(YesterdayDone, Overdue, DueToday);
}
