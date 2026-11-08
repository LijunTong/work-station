using WorkStation.Core.Models;
using WorkStation.Core.Reports;

namespace WorkStation.Tests;

/// <summary>三类报告提醒的触发判定与去重。</summary>
public class ReportReminderPlannerTests
{
    // hour 默认 0：只关心「哪一天」的用例不必带时刻
    private static DateTime At(int month, int day, int hour = 0, int minute = 0) =>
        new(2026, month, day, hour, minute, 0, DateTimeKind.Local);

    private static ReportReminderEntry Entry(
        bool enabled = true,
        string time = "09:00",
        int dayOfWeek = 5,
        int dayOfMonth = 31) => new()
    {
        Enabled = enabled,
        Time = time,
        DayOfWeek = dayOfWeek,
        DayOfMonth = dayOfMonth
    };

    [Fact]
    public void IsValidTime_AcceptsOnlyHhMm()
    {
        Assert.True(ReportReminderPlanner.IsValidTime("09:00"));
        Assert.True(ReportReminderPlanner.IsValidTime("23:30"));
        Assert.False(ReportReminderPlanner.IsValidTime("9:00"));
        Assert.False(ReportReminderPlanner.IsValidTime("25:00"));
        Assert.False(ReportReminderPlanner.IsValidTime(null));
    }

    [Fact]
    public void IsoDayOfWeek_UsesMondayAsOne()
    {
        Assert.Equal(1, ReportReminderPlanner.IsoDayOfWeek(At(11, 2)));   // 周一
        Assert.Equal(5, ReportReminderPlanner.IsoDayOfWeek(At(11, 6)));   // 周五
        Assert.Equal(7, ReportReminderPlanner.IsoDayOfWeek(At(11, 8)));   // 周日
    }

    [Fact]
    public void ResolveMonthDay_FallsBackToLastDayOfMonth()
    {
        // 2026 年 11 月只有 30 天
        Assert.Equal(30, ReportReminderPlanner.ResolveMonthDay(31, At(11, 8)));
        Assert.Equal(28, ReportReminderPlanner.ResolveMonthDay(31, At(2, 10)));
        Assert.Equal(15, ReportReminderPlanner.ResolveMonthDay(15, At(11, 8)));
    }

    [Fact]
    public void Daily_FiresOnceAfterTime()
    {
        var entry = Entry(time: "09:00");

        Assert.False(ReportReminderPlanner.ShouldFire(ReportKind.Daily, entry, At(11, 8, 8, 59), null));
        Assert.True(ReportReminderPlanner.ShouldFire(ReportKind.Daily, entry, At(11, 8, 9, 0), null));
    }

    [Fact]
    public void Daily_DoesNotFireTwiceInSameDay()
    {
        var entry = Entry(time: "09:00");
        var key = ReportReminderPlanner.PeriodKey(ReportKind.Daily, At(11, 8));

        Assert.False(ReportReminderPlanner.ShouldFire(ReportKind.Daily, entry, At(11, 8, 18, 0), key));
        Assert.True(ReportReminderPlanner.ShouldFire(ReportKind.Daily, entry, At(11, 9, 9, 0), key));
    }

    [Fact]
    public void Weekly_FiresOnlyOnConfiguredWeekday()
    {
        // 2026-11-06 是周五
        var entry = Entry(time: "17:00", dayOfWeek: 5);

        Assert.False(ReportReminderPlanner.ShouldFire(ReportKind.Weekly, entry, At(11, 5, 17, 0), null));
        Assert.True(ReportReminderPlanner.ShouldFire(ReportKind.Weekly, entry, At(11, 6, 17, 0), null));
    }

    [Fact]
    public void Weekly_KeyIsMondayOfThatWeek()
    {
        Assert.Equal("2026-11-02", ReportReminderPlanner.PeriodKey(ReportKind.Weekly, At(11, 6)));
        Assert.Equal("2026-11-02", ReportReminderPlanner.PeriodKey(ReportKind.Weekly, At(11, 8)));
    }

    [Fact]
    public void Weekly_DoesNotFireAgainInSameWeek()
    {
        var entry = Entry(time: "17:00", dayOfWeek: 5);
        var key = ReportReminderPlanner.PeriodKey(ReportKind.Weekly, At(11, 6));

        Assert.False(ReportReminderPlanner.ShouldFire(ReportKind.Weekly, entry, At(11, 6, 20, 0), key));
        // 下一周的周五：键变了，可以再弹
        Assert.True(ReportReminderPlanner.ShouldFire(ReportKind.Weekly, entry, At(11, 13, 17, 0), key));
    }

    [Fact]
    public void Monthly_FiresOnResolvedDayOfMonth()
    {
        var entry = Entry(time: "17:00", dayOfMonth: 31);

        // 11 月只有 30 天，设定 31 号 → 落到 30 号
        Assert.True(ReportReminderPlanner.ShouldFire(ReportKind.Monthly, entry, At(11, 30, 17, 0), null));
        Assert.False(ReportReminderPlanner.ShouldFire(ReportKind.Monthly, entry, At(11, 29, 17, 0), null));
    }

    [Fact]
    public void Monthly_KeyIsFirstDayOfMonth()
    {
        Assert.Equal("2026-11-01", ReportReminderPlanner.PeriodKey(ReportKind.Monthly, At(11, 30)));
    }

    [Fact]
    public void DisabledEntry_NeverFires()
    {
        var entry = Entry(enabled: false, time: "09:00");

        Assert.False(ReportReminderPlanner.ShouldFire(ReportKind.Daily, entry, At(11, 8, 10, 0), null));
    }
}
