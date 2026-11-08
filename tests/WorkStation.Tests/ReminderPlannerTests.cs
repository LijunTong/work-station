using WorkStation.Core.Models;
using WorkStation.Core.Reminders;

namespace WorkStation.Tests;

/// <summary>提醒的纯计算：提前量与重复规则的下一次。</summary>
public class ReminderPlannerTests
{
    [Theory]
    [InlineData(60, "2026-11-05T17:00")]
    [InlineData(15, "2026-11-05T17:45")]
    [InlineData(1440, "2026-11-04T18:00")]
    [InlineData(180, "2026-11-05T15:00")]
    public void ComputeTaskRemindAt_SubtractsLead(int lead, string expected)
    {
        Assert.Equal(expected, ReminderPlanner.ComputeTaskRemindAt("2026-11-05T18:00", lead));
    }

    [Fact]
    public void ComputeTaskRemindAt_ZeroLeadMeansNoReminder()
    {
        // 0 是任务的默认值：按「准时提醒」处理会让每个任务到点都弹一次
        Assert.Null(ReminderPlanner.ComputeTaskRemindAt("2026-11-05T18:00", 0));
        Assert.Null(ReminderPlanner.ComputeTaskRemindAt("2026-11-05T18:00", -5));
    }

    [Fact]
    public void ComputeTaskRemindAt_IgnoresBrokenDue()
    {
        Assert.Null(ReminderPlanner.ComputeTaskRemindAt(null, 30));
        Assert.Null(ReminderPlanner.ComputeTaskRemindAt("", 30));
        Assert.Null(ReminderPlanner.ComputeTaskRemindAt("2026-11-05 18:00", 30));
    }

    [Fact]
    public void ComputeTaskRemindAt_CrossesMonthBoundary()
    {
        Assert.Equal("2026-10-31T23:30", ReminderPlanner.ComputeTaskRemindAt("2026-11-01T00:30", 60));
    }

    [Fact]
    public void NextOccurrence_Daily_IsStrictlyAfterNow()
    {
        Assert.Equal("2026-11-06T09:00",
            ReminderPlanner.NextOccurrence("2026-11-05T09:00", RepeatRule.Daily, "2026-11-05T09:00"));
        Assert.Equal("2026-11-06T09:00",
            ReminderPlanner.NextOccurrence("2026-11-05T09:00", RepeatRule.Daily, "2026-11-05T18:30"));
    }

    [Fact]
    public void NextOccurrence_SkipsMissedCycles()
    {
        // 应用关了三周再打开：下一步应该落在明天，而不是三周前的某一天
        Assert.Equal("2026-11-06T09:00",
            ReminderPlanner.NextOccurrence("2026-10-15T09:00", RepeatRule.Daily, "2026-11-05T10:00"));
        // 每周提醒从 10-13（周二）往后跳到 11-10，而不是跳过一周落到 11-03
        Assert.Equal("2026-11-10T09:00",
            ReminderPlanner.NextOccurrence("2026-10-13T09:00", RepeatRule.Weekly, "2026-11-05T10:00"));
    }

    [Fact]
    public void NextOccurrence_NotYetDue_ReturnsSelf()
    {
        Assert.Equal("2026-11-06T09:00",
            ReminderPlanner.NextOccurrence("2026-11-06T09:00", RepeatRule.Daily, "2026-11-05T10:00"));
    }

    [Fact]
    public void NextOccurrence_NoRepeat_ReturnsNull()
    {
        Assert.Null(ReminderPlanner.NextOccurrence("2026-11-06T09:00", RepeatRule.None, "2026-11-05T10:00"));
    }

    [Fact]
    public void IsDue_ComparesWallClockStrings()
    {
        Assert.True(ReminderPlanner.IsDue("2026-11-05T09:00", "2026-11-05T09:00"));
        Assert.True(ReminderPlanner.IsDue("2026-11-05T08:59", "2026-11-05T09:00"));
        Assert.False(ReminderPlanner.IsDue("2026-11-05T09:01", "2026-11-05T09:00"));
    }
}
