using WorkStation.Core.Models;
using WorkStation.Core.Reports;

namespace WorkStation.Tests;

/// <summary>报告周期：日 / 周 / 月的起止日，以及前后翻页。</summary>
public class ReportPeriodsTests
{
    private static DateTime At(int month, int day, int hour = 12) =>
        new(2026, month, day, hour, 0, 0, DateTimeKind.Local);

    [Fact]
    public void Daily_CoversTodayOnly()
    {
        var period = ReportPeriods.Daily(At(11, 8));

        Assert.Equal(ReportKind.Daily, period.Kind);
        Assert.Equal("2026-11-08", period.PeriodStart);
        Assert.Equal("2026-11-08", period.PeriodEnd);
    }

    /// <summary>周口径以周一为首日：11-08 是周日，那一周是 11-02 ~ 11-08。</summary>
    [Fact]
    public void Weekly_StartsOnMonday()
    {
        var period = ReportPeriods.Weekly(At(11, 8));

        Assert.Equal("2026-11-02", period.PeriodStart);
        Assert.Equal("2026-11-08", period.PeriodEnd);
    }

    [Fact]
    public void Weekly_OnFridayStillCoversWholeWeek()
    {
        var period = ReportPeriods.Weekly(At(11, 6));

        Assert.Equal("2026-11-02", period.PeriodStart);
        Assert.Equal("2026-11-08", period.PeriodEnd);
    }

    [Fact]
    public void Monthly_CoversFirstToLastDay()
    {
        var period = ReportPeriods.Monthly(At(11, 8));

        Assert.Equal("2026-11-01", period.PeriodStart);
        Assert.Equal("2026-11-30", period.PeriodEnd);
    }

    [Fact]
    public void Monthly_FebruaryEndsOn28thIn2026()
    {
        var period = ReportPeriods.Monthly(At(2, 10));

        Assert.Equal("2026-02-01", period.PeriodStart);
        Assert.Equal("2026-02-28", period.PeriodEnd);
    }

    [Fact]
    public void Current_PicksByKind()
    {
        Assert.Equal(ReportKind.Daily, ReportPeriods.Current(ReportKind.Daily, At(11, 8)).Kind);
        Assert.Equal(ReportKind.Weekly, ReportPeriods.Current(ReportKind.Weekly, At(11, 8)).Kind);
        Assert.Equal(ReportKind.Monthly, ReportPeriods.Current(ReportKind.Monthly, At(11, 8)).Kind);
    }

    [Fact]
    public void FromStart_DerivesPeriodEnd()
    {
        Assert.Equal("2026-11-08", ReportPeriods.FromStart(ReportKind.Weekly, "2026-11-02").PeriodEnd);
        Assert.Equal("2026-11-30", ReportPeriods.FromStart(ReportKind.Monthly, "2026-11-01").PeriodEnd);
        Assert.Equal("2026-11-08", ReportPeriods.FromStart(ReportKind.Daily, "2026-11-08").PeriodEnd);
    }

    [Fact]
    public void Shift_MovesByOneStepOfItsKind()
    {
        var daily = ReportPeriods.Daily(At(11, 8));
        Assert.Equal("2026-11-09", ReportPeriods.Shift(daily, 1).PeriodStart);

        var weekly = ReportPeriods.Weekly(At(11, 8));
        var nextWeek = ReportPeriods.Shift(weekly, 1);
        Assert.Equal("2026-11-09", nextWeek.PeriodStart);
        Assert.Equal("2026-11-15", nextWeek.PeriodEnd);

        var monthly = ReportPeriods.Monthly(At(11, 8));
        Assert.Equal("2026-12-01", ReportPeriods.Shift(monthly, 1).PeriodStart);
        Assert.Equal("2026-12-31", ReportPeriods.Shift(monthly, 1).PeriodEnd);
    }

    [Fact]
    public void Shift_MonthlyCrossesYearBoundary()
    {
        var december = ReportPeriods.Monthly(At(12, 8));

        var january = ReportPeriods.Shift(december, 1);

        Assert.Equal("2027-01-01", january.PeriodStart);
        Assert.Equal("2027-01-31", january.PeriodEnd);
    }

    [Fact]
    public void Label_And_Range()
    {
        Assert.Equal("日报", ReportPeriods.Label(ReportKind.Daily));
        Assert.Equal("周报", ReportPeriods.Label(ReportKind.Weekly));
        Assert.Equal("月报", ReportPeriods.Label(ReportKind.Monthly));

        Assert.Equal("2026-11-08", ReportPeriods.Range(ReportPeriods.Daily(At(11, 8))));
        Assert.Equal("2026-11-02 ~ 2026-11-08", ReportPeriods.Range(ReportPeriods.Weekly(At(11, 8))));
    }
}
