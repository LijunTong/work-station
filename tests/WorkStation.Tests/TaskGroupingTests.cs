using WorkStation.Core;
using WorkStation.Core.Models;

namespace WorkStation.Tests;

public class TaskGroupingTests
{
    private static TaskItem Make(string? dueAt) => new()
    {
        Id = "task-1",
        Title = "示例任务",
        DueAt = dueAt,
        CreatedAt = "2026-11-05T00:00:00.000Z",
        UpdatedAt = "2026-11-05T00:00:00.000Z"
    };

    private static DateTime At(int year, int month, int day, int hour = 0, int minute = 0) =>
        new(year, month, day, hour, minute, 0, DateTimeKind.Local);

    [Fact]
    public void Group_NoDueAt_IsSomeday()
    {
        Assert.Equal(TaskGroup.Someday, TaskGrouping.Group(Make(null), At(2026, 11, 5, 10, 0)));
    }

    [Fact]
    public void Group_TodayButTimePassed_IsOverdue()
    {
        // 核心回归：Electron 版用日历日分组，导致今天 09:00 到期、现在 10:00 的任务
        // 落在「今日」组却被标红，「逾期」组计数与 overdueCount() 对不上。
        Assert.Equal(TaskGroup.Overdue, TaskGrouping.Group(Make("2026-11-05T09:00"), At(2026, 11, 5, 10, 0)));
    }

    [Fact]
    public void Group_TodayAndTimeNotReached_IsToday()
    {
        Assert.Equal(TaskGroup.Today, TaskGrouping.Group(Make("2026-11-05T23:00"), At(2026, 11, 5, 10, 0)));
    }

    [Fact]
    public void Group_DueTimeEqualsNow_IsNotOverdue()
    {
        Assert.Equal(TaskGroup.Today, TaskGrouping.Group(Make("2026-11-05T10:00"), At(2026, 11, 5, 10, 0)));
    }

    [Fact]
    public void Group_Yesterday_IsOverdue()
    {
        Assert.Equal(TaskGroup.Overdue, TaskGrouping.Group(Make("2026-11-04T18:00"), At(2026, 11, 5, 10, 0)));
    }

    [Fact]
    public void Group_ThisSunday_IsWeek()
    {
        // 2026-11-05 是周四，本周（周一至周日）到 11-08
        Assert.Equal(TaskGroup.Week, TaskGrouping.Group(Make("2026-11-08T23:00"), At(2026, 11, 5, 10, 0)));
    }

    [Fact]
    public void Group_NextMonday_IsLater()
    {
        Assert.Equal(TaskGroup.Later, TaskGrouping.Group(Make("2026-11-09T09:00"), At(2026, 11, 5, 10, 0)));
    }

    [Fact]
    public void Group_WhenNowIsSunday_LaterSameDay_IsToday()
    {
        // 2026-11-08 是周日：本周只剩今天，同一天未到点的任务归「今日」而不是「本周」
        Assert.Equal(TaskGroup.Today, TaskGrouping.Group(Make("2026-11-08T23:00"), At(2026, 11, 8, 10, 0)));
    }

    [Fact]
    public void Group_WhenNowIsSunday_NextDayIsLater()
    {
        Assert.Equal(TaskGroup.Later, TaskGrouping.Group(Make("2026-11-09T09:00"), At(2026, 11, 8, 10, 0)));
    }

    [Fact]
    public void Group_WhenNowIsMonday_SundayDueIsWeek()
    {
        // 2026-11-09 是周一，本周到 11-15
        Assert.Equal(TaskGroup.Week, TaskGrouping.Group(Make("2026-11-15T23:00"), At(2026, 11, 9, 10, 0)));
    }

    [Fact]
    public void Group_WhenNowIsSaturday_SundayDueIsWeek()
    {
        // 2026-11-14 是周六，本周还剩周日一天
        Assert.Equal(TaskGroup.Week, TaskGrouping.Group(Make("2026-11-15T09:00"), At(2026, 11, 14, 10, 0)));
    }

    [Fact]
    public void Group_CrossesMonthBoundary_UsesWeekEndSunday()
    {
        // 2026-01-30 是周五，本周到 2026-02-01（周日）
        Assert.Equal(TaskGroup.Week, TaskGrouping.Group(Make("2026-02-01T09:00"), At(2026, 1, 30, 10, 0)));
        Assert.Equal(TaskGroup.Later, TaskGrouping.Group(Make("2026-02-02T09:00"), At(2026, 1, 30, 10, 0)));
    }

    [Fact]
    public void Group_CrossesYearBoundary_UsesWeekEndSunday()
    {
        // 2025-12-31 是周三，本周到 2026-01-04（周日）
        Assert.Equal(TaskGroup.Week, TaskGrouping.Group(Make("2026-01-04T09:00"), At(2025, 12, 31, 10, 0)));
        Assert.Equal(TaskGroup.Later, TaskGrouping.Group(Make("2026-01-05T09:00"), At(2025, 12, 31, 10, 0)));
    }

    [Fact]
    public void WeekEndDate_ReturnsSundayOfCurrentWeek()
    {
        Assert.Equal("2026-11-08", TaskGrouping.WeekEndDate(At(2026, 11, 5, 10, 0)));
        Assert.Equal("2026-11-08", TaskGrouping.WeekEndDate(At(2026, 11, 8, 10, 0)));
        Assert.Equal("2026-11-15", TaskGrouping.WeekEndDate(At(2026, 11, 9, 10, 0)));
    }

    [Fact]
    public void Group_InvalidDueAtFormat_Throws()
    {
        Assert.Throws<ArgumentException>(() =>
            TaskGrouping.Group(Make("2026-11-05"), At(2026, 11, 5, 10, 0)));
    }
}
