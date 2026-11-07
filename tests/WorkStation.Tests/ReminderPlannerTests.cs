using WorkStation.Core.Models;
using WorkStation.Core.Reminders;
using WorkStation.Core.Time;

namespace WorkStation.Tests;

/// <summary>提醒的纯计算：提前量、重复规则的下一次、站会提醒的判定与文案。</summary>
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

    [Theory]
    [InlineData("09:00", true)]
    [InlineData("23:59", true)]
    [InlineData("9:00", false)]
    [InlineData("24:00", false)]
    [InlineData("", false)]
    public void IsValidTime_Accepts24HourClock(string value, bool expected)
    {
        Assert.Equal(expected, DailyReviewPlanner.IsValidTime(value));
    }

    [Fact]
    public void ShouldFire_RespectsEnableTimeAndAlreadyFiredToday()
    {
        var enabled = new DailyReviewSettings { Enabled = true, Time = "09:00" };

        Assert.False(DailyReviewPlanner.ShouldFire(enabled, "2026-11-05T08:59", null));
        Assert.True(DailyReviewPlanner.ShouldFire(enabled, "2026-11-05T09:00", null));
        Assert.True(DailyReviewPlanner.ShouldFire(enabled, "2026-11-05T21:00", "2026-11-04"));

        // 今天已经发过：哪怕时间已过也不再发
        Assert.False(DailyReviewPlanner.ShouldFire(enabled, "2026-11-05T21:00", "2026-11-05"));
        Assert.False(DailyReviewPlanner.ShouldFire(enabled with { Enabled = false }, "2026-11-05T21:00", null));

        // 设置里写了非法时间：退回默认的 09:00，而不是整条提醒失灵
        Assert.True(DailyReviewPlanner.ShouldFire(new DailyReviewSettings { Time = "25:00" }, "2026-11-05T10:00", null));
    }

    [Fact]
    public void ComputeCounts_UsesTaskGroupingSemantics()
    {
        var now = LocalTime.ToLocalDateTime(DateTime.Today.AddHours(12));

        // 必须从「本地昨天」换算成 UTC，不能拿 UtcNow.Date 直接减一天：
        // 本地处于 UTC+8 时，本地 00:00–08:00 这段时间的 UtcNow 还是前一天，
        // 算出来的「昨天」会比真正的本地昨天再早一天，测试于是只在白天通过。
        var yesterdayInstant = DateTime.Today.AddDays(-1).AddHours(12)
            .ToUniversalTime()
            .ToString("yyyy-MM-ddTHH:mm:ss.fffZ", System.Globalization.CultureInfo.InvariantCulture);

        var tasks = new List<TaskItem>
        {
            new()
            {
                Id = "done-yesterday", Title = "昨天完成的", Status = TaskStatus.Done,
                CreatedAt = "", UpdatedAt = "", CompletedAt = yesterdayInstant
            },
            new()
            {
                Id = "overdue", Title = "逾期的", Status = TaskStatus.Todo,
                CreatedAt = "", UpdatedAt = "", DueAt = LocalTime.ToLocalDateTime(DateTime.Today.AddHours(9))
            },
            new()
            {
                Id = "today", Title = "今晚到期", Status = TaskStatus.Doing,
                CreatedAt = "", UpdatedAt = "", DueAt = LocalTime.ToLocalDateTime(DateTime.Today.AddHours(18))
            },
            new()
            {
                Id = "later", Title = "下周的", Status = TaskStatus.Todo,
                CreatedAt = "", UpdatedAt = "", DueAt = LocalTime.ToLocalDateTime(DateTime.Today.AddDays(5))
            },
            new()
            {
                Id = "canceled", Title = "取消的", Status = TaskStatus.Canceled,
                CreatedAt = "", UpdatedAt = "", DueAt = LocalTime.ToLocalDateTime(DateTime.Today.AddHours(8))
            }
        };

        var counts = DailyReviewPlanner.ComputeCounts(tasks, now);

        Assert.Equal(1, counts.YesterdayDone);
        Assert.Equal(1, counts.Overdue);
        Assert.Equal(1, counts.DueToday);
        Assert.Contains("昨日完成 1 项", counts.Message);
    }
}
