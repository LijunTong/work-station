using System.Globalization;
using WorkStation.Core.Board;
using WorkStation.Core.Models;

namespace WorkStation.Tests;

/// <summary>看板口径：四列顺序、已完成窗口、时间筛选、概览计数。</summary>
public class TaskBoardTests
{
    // 固定时钟：2026-11-05 是周四，本周结束于 11-08（周日）
    private static readonly DateTime Now = new(2026, 11, 5, 10, 0, 0, DateTimeKind.Local);

    private static TaskItem Task(
        string title,
        TaskStatus status = TaskStatus.Todo,
        string? dueAt = null,
        string? completedAt = null) => new()
    {
        Id = title,
        Title = title,
        Status = status,
        DueAt = dueAt,
        CompletedAt = completedAt,
        CreatedAt = "2026-11-01T00:00:00.000Z",
        UpdatedAt = "2026-11-01T00:00:00.000Z"
    };

    private static string Iso(DateTime value) =>
        value.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture);

    [Fact]
    public void ColumnOrder_IsFixed()
    {
        Assert.Equal(
            [TaskStatus.Todo, TaskStatus.Doing, TaskStatus.Blocked, TaskStatus.Done],
            TaskBoard.ColumnOrder);
    }

    /// <summary>
    /// 「本月」口径：到期日在本月内、且还没到点。
    /// 与「本周」同一套思路 —— 逾期的已经在「逾期」里，不在这里重复计数。
    /// </summary>
    [Fact]
    public void MonthScope_CountsUpcomingTasksDueThisMonth()
    {
        TaskItem[] tasks =
        [
            Task("本月内", dueAt: "2026-11-25T18:00"),
            Task("本月内但已过点", dueAt: "2026-11-04T09:00"),
            Task("下个月", dueAt: "2026-12-02T09:00"),
            Task("无期限")
        ];

        var overview = TaskBoard.ComputeOverview(tasks, Now);

        Assert.Equal(1, overview.Month);
        Assert.True(TaskBoard.MatchesTimeScope(tasks[0], BoardTimeScope.Month, Now));
        Assert.False(TaskBoard.MatchesTimeScope(tasks[1], BoardTimeScope.Month, Now));
        Assert.False(TaskBoard.MatchesTimeScope(tasks[2], BoardTimeScope.Month, Now));
        Assert.False(TaskBoard.MatchesTimeScope(tasks[3], BoardTimeScope.Month, Now));
    }

    /// <summary>概览条上的本月计数与筛选结果必须一致（点进去数量要对得上）。</summary>
    [Fact]
    public void MonthScope_CountMatchesFilter()
    {
        TaskItem[] tasks =
        [
            Task("一", dueAt: "2026-11-06T18:00"),
            Task("二", dueAt: "2026-11-20T18:00"),
            Task("三", dueAt: "2026-12-01T18:00")
        ];

        var overview = TaskBoard.ComputeOverview(tasks, Now);
        var matched = tasks.Count(t => TaskBoard.MatchesTimeScope(t, BoardTimeScope.Month, Now));

        Assert.Equal(overview.Month, matched);
    }

    [Fact]
    public void Overview_CountsByGroup_ExcludingCanceled()
    {
        TaskItem[] tasks =
        [
            Task("今日", dueAt: "2026-11-05T18:00"),
            Task("逾期", dueAt: "2026-11-04T09:00"),
            Task("本周", dueAt: "2026-11-07T09:00"),
            Task("以后", dueAt: "2026-12-01T09:00"),
            Task("无期限"),
            Task("阻塞无期限", status: TaskStatus.Blocked),
            Task("取消", status: TaskStatus.Canceled, dueAt: "2026-11-05T09:00"),
            Task("完成", status: TaskStatus.Done, completedAt: Iso(Now.AddDays(-1)))
        ];

        var overview = TaskBoard.ComputeOverview(tasks, Now);

        Assert.Equal(1, overview.Today);
        Assert.Equal(1, overview.Overdue);
        Assert.Equal(1, overview.Week);
        Assert.Equal(1, overview.Blocked);
    }

    [Fact]
    public void DoneWindow_SevenDays()
    {
        Assert.True(TaskBoard.IsInDoneWindow(
            Task("刚完成", TaskStatus.Done, completedAt: Iso(Now.AddDays(-3))), Now));

        Assert.False(TaskBoard.IsInDoneWindow(
            Task("很久以前", TaskStatus.Done, completedAt: Iso(Now.AddDays(-10))), Now));

        // 没时间戳的历史数据不隐藏：宁可多显示一条，也不要让用户以为任务丢了
        Assert.True(TaskBoard.IsInDoneWindow(Task("无时间戳", TaskStatus.Done), Now));

        // 未完成的任务永远不算「已完成窗口」里的
        Assert.False(TaskBoard.IsInDoneWindow(Task("待办"), Now));
    }

    [Fact]
    public void DoneWindow_CanBeWidened()
    {
        var task = Task("三十天前", TaskStatus.Done, completedAt: Iso(Now.AddDays(-30)));

        Assert.False(TaskBoard.IsInDoneWindow(task, Now));
        Assert.True(TaskBoard.IsInDoneWindow(task, Now, days: 60));
    }

    [Fact]
    public void TimeScope_MatchesTaskGrouping()
    {
        var today = Task("今日", dueAt: "2026-11-05T18:00");
        var overdue = Task("逾期", dueAt: "2026-11-05T09:00");
        var week = Task("本周", dueAt: "2026-11-06T09:00");

        Assert.True(TaskBoard.MatchesTimeScope(today, BoardTimeScope.Today, Now));
        Assert.False(TaskBoard.MatchesTimeScope(overdue, BoardTimeScope.Today, Now));

        Assert.True(TaskBoard.MatchesTimeScope(overdue, BoardTimeScope.Overdue, Now));
        Assert.False(TaskBoard.MatchesTimeScope(week, BoardTimeScope.Overdue, Now));

        Assert.True(TaskBoard.MatchesTimeScope(week, BoardTimeScope.Week, Now));
        Assert.False(TaskBoard.MatchesTimeScope(today, BoardTimeScope.Week, Now));

        Assert.True(TaskBoard.MatchesTimeScope(today, BoardTimeScope.All, Now));
    }

    [Fact]
    public void TimeScope_KeepsDoneTasksVisible()
    {
        // 已完成任务的截止时间早已过去，跟着时间筛选会被整列滤空
        var done = Task("早就完成", TaskStatus.Done, dueAt: "2026-10-01T09:00");

        Assert.True(TaskBoard.MatchesTimeScope(done, BoardTimeScope.Today, Now));
        Assert.True(TaskBoard.MatchesTimeScope(done, BoardTimeScope.Overdue, Now));
    }

    [Fact]
    public void TimeScope_HidesCanceled()
    {
        var canceled = Task("取消", TaskStatus.Canceled, dueAt: "2026-11-05T09:00");

        // 「全部」不过滤时间，取消任务由调用方在分列前剔除；
        // 一旦叠加了时间口径，取消任务就不该再命中
        Assert.True(TaskBoard.MatchesTimeScope(canceled, BoardTimeScope.All, Now));
        Assert.False(TaskBoard.MatchesTimeScope(canceled, BoardTimeScope.Today, Now));
        Assert.False(TaskBoard.MatchesTimeScope(canceled, BoardTimeScope.Overdue, Now));
    }

    [Fact]
    public void IsOpen_ExcludesDoneAndCanceled()
    {
        Assert.True(TaskBoard.IsOpen(Task("待办")));
        Assert.True(TaskBoard.IsOpen(Task("阻塞", TaskStatus.Blocked)));
        Assert.False(TaskBoard.IsOpen(Task("完成", TaskStatus.Done)));
        Assert.False(TaskBoard.IsOpen(Task("取消", TaskStatus.Canceled)));
    }
}
