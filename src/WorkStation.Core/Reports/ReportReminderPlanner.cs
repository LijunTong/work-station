using System.Globalization;
using WorkStation.Core.Models;
using WorkStation.Core.Time;

namespace WorkStation.Core.Reports;

/// <summary>
/// 报告提醒的触发判定 —— 纯函数。
///
/// 报告提醒不落 reminders 表（每天/每周/每月一次，没有「处理」的历史意义），
/// 用「上次触发的周期键」去重：日报的键是今天、周报是本周一、月报是本月 1 号。
/// </summary>
public static class ReportReminderPlanner
{
    public const string DefaultTime = "09:00";

    public static bool IsValidTime(string? value) =>
        value is not null
        && TimeSpan.TryParseExact(value, @"hh\:mm", CultureInfo.InvariantCulture, out _);

    /// <summary>ISO 星期几：1=周一 … 7=周日。.NET 的 DayOfWeek 是 0=周日，要转。</summary>
    public static int IsoDayOfWeek(DateTime date) => ((int)date.DayOfWeek + 6) % 7 + 1;

    /// <summary>设定的几号在当月不存在时，落到当月最后一天（31 号 → 2 月 28 号）。</summary>
    public static int ResolveMonthDay(int requestedDay, DateTime localNow)
    {
        var clamped = Math.Clamp(requestedDay, 1, 31);
        var daysInMonth = DateTime.DaysInMonth(localNow.Year, localNow.Month);
        return Math.Min(clamped, daysInMonth);
    }

    /// <summary>
    /// 去重键：这个周期的起点。
    /// 日报是今天，周报是本周一，月报是本月 1 号。
    /// </summary>
    public static string PeriodKey(ReportKind kind, DateTime now) =>
        ReportPeriods.Current(kind, now).PeriodStart;

    /// <summary>
    /// 该不该在这一拍触发。
    ///
    /// lastFiredKey 是上次触发的周期键：与当前周期键相同就说明这个周期已经报过了。
    /// </summary>
    public static bool ShouldFire(ReportKind kind, ReportReminderEntry entry, DateTime now, string? lastFiredKey)
    {
        if (!entry.Enabled) return false;
        if (!TryParseTime(entry.Time, out var at)) return false;

        var local = LocalTime.AsLocal(now);
        if (local.TimeOfDay < at) return false;

        var key = PeriodKey(kind, local);
        if (string.Equals(lastFiredKey, key, StringComparison.Ordinal)) return false;

        return kind switch
        {
            ReportKind.Daily => true,
            ReportKind.Weekly => IsoDayOfWeek(local) == entry.DayOfWeek,
            _ => local.Day == ResolveMonthDay(entry.DayOfMonth, local)
        };
    }

    private static bool TryParseTime(string? value, out TimeSpan time)
    {
        if (value is not null
            && TimeSpan.TryParseExact(value, @"hh\:mm", CultureInfo.InvariantCulture, out time))
        {
            return true;
        }

        time = TimeSpan.ParseExact(DefaultTime, @"hh\:mm", CultureInfo.InvariantCulture);
        return false;
    }
}
