using System.Globalization;
using WorkStation.Core;
using WorkStation.Core.Models;
using WorkStation.Core.Time;

namespace WorkStation.Core.Reports;

/// <summary>
/// 报告周期计算 —— 纯函数，直接单测。
///
/// 周口径复用 <see cref="TaskGrouping.WeekEndDate"/>（周一起始、周日收尾），
/// 起始日由结束日减 6 天得出：另立一套算法，看板的「本周」和周报的「本周」
/// 迟早会在某个周日差一天。
/// </summary>
public static class ReportPeriods
{
    public static ReportPeriod Daily(DateTime now)
    {
        var date = LocalTime.ToLocalDate(LocalTime.AsLocal(now));
        return new ReportPeriod { Kind = ReportKind.Daily, PeriodStart = date, PeriodEnd = date };
    }

    public static ReportPeriod Weekly(DateTime now)
    {
        var end = TaskGrouping.WeekEndDate(now);
        var startDate = ParseDate(end).AddDays(-6);
        return new ReportPeriod
        {
            Kind = ReportKind.Weekly,
            PeriodStart = LocalTime.ToLocalDate(startDate),
            PeriodEnd = end
        };
    }

    public static ReportPeriod Monthly(DateTime now)
    {
        var local = LocalTime.AsLocal(now);
        var start = new DateTime(local.Year, local.Month, 1);
        var end = start.AddMonths(1).AddDays(-1);
        return new ReportPeriod
        {
            Kind = ReportKind.Monthly,
            PeriodStart = LocalTime.ToLocalDate(start),
            PeriodEnd = LocalTime.ToLocalDate(end)
        };
    }

    /// <summary>按类型取当前周期。</summary>
    public static ReportPeriod Current(ReportKind kind, DateTime now) => kind switch
    {
        ReportKind.Daily => Daily(now),
        ReportKind.Weekly => Weekly(now),
        _ => Monthly(now)
    };

    /// <summary>
    /// 按周期起点反推整个周期。起点必须是该周期的首日
    /// （日报任意一天、周报的周一、月报的 1 号）—— 调用方一律用
    /// <see cref="Current"/> / <see cref="Shift"/> 产生起点，不会出错。
    /// </summary>
    public static ReportPeriod FromStart(ReportKind kind, string periodStart)
    {
        var start = ParseDate(periodStart);
        var end = kind switch
        {
            ReportKind.Daily => start,
            ReportKind.Weekly => start.AddDays(6),
            _ => start.AddMonths(1).AddDays(-1)
        };

        return new ReportPeriod
        {
            Kind = kind,
            PeriodStart = LocalTime.ToLocalDate(start),
            PeriodEnd = LocalTime.ToLocalDate(end)
        };
    }

    /// <summary>前一个 / 后一个周期：日报按天，周报按周，月报按月。</summary>
    public static ReportPeriod Shift(ReportPeriod period, int delta)
    {
        var start = ParseDate(period.PeriodStart);
        var shifted = period.Kind switch
        {
            ReportKind.Daily => start.AddDays(delta),
            ReportKind.Weekly => start.AddDays(7 * delta),
            _ => start.AddMonths(delta)
        };

        return FromStart(period.Kind, LocalTime.ToLocalDate(shifted));
    }

    /// <summary>类型中文名：日报 / 周报 / 月报。</summary>
    public static string Label(ReportKind kind) => kind switch
    {
        ReportKind.Daily => "日报",
        ReportKind.Weekly => "周报",
        _ => "月报"
    };

    /// <summary>周期的文字区间：单日只写日期，其余写「起 ~ 止」。</summary>
    public static string Range(ReportPeriod period) =>
        period.PeriodStart == period.PeriodEnd
            ? period.PeriodStart
            : $"{period.PeriodStart} ~ {period.PeriodEnd}";

    private static DateTime ParseDate(string value) =>
        DateTime.ParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture);
}
