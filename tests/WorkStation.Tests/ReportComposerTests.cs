using WorkStation.Core.Models;
using WorkStation.Core.Reports;

namespace WorkStation.Tests;

/// <summary>报告的自动汇总：完成 / 未完成 / 逾期三个口径与 Markdown 输出。</summary>
public class ReportComposerTests
{
    private static DateTime At(int month, int day, int hour = 12) =>
        new(2026, month, day, hour, 0, 0, DateTimeKind.Local);

    private static TaskItem Task(
        string title,
        TaskStatus status = TaskStatus.Todo,
        string? due = null,
        string? completed = null,
        string? projectId = null) => new()
    {
        Id = Guid.NewGuid().ToString("N"),
        ProjectId = projectId,
        Title = title,
        Status = status,
        Priority = Priority.None,
        DueAt = due,
        CompletedAt = completed,
        CreatedAt = "2026-11-01T00:00:00.000Z",
        UpdatedAt = "2026-11-01T00:00:00.000Z"
    };

    private static readonly Project Project = new()
    {
        Id = "p1",
        Name = "官网改版",
        CreatedAt = "2026-11-01T00:00:00.000Z",
        UpdatedAt = "2026-11-01T00:00:00.000Z"
    };

    [Fact]
    public void ComputeStats_CountsCompletedCarriedOverAndOverdue()
    {
        var tasks = new[]
        {
            Task("已做完", TaskStatus.Done, completed: "2026-11-05T02:00:00.000Z"),
            Task("还欠着", TaskStatus.Doing, due: "2026-11-05T18:00"),
            Task("早就过期了", TaskStatus.Todo, due: "2026-11-03T18:00")
        };

        // 「还欠着」截止 11-05、当天是 11-05 —— 截止日当天不算逾期；
        // 「早就过期了」截止 11-03 —— 早于今天才算逾期。
        var stats = ReportComposer.ComputeStats(ReportPeriods.Weekly(At(11, 5)), tasks, At(11, 5));

        Assert.Equal(1, stats.Completed);
        Assert.Equal(2, stats.CarriedOver);
        Assert.Equal(1, stats.Overdue);
        Assert.Equal(3, stats.Total);
        Assert.Equal(33, stats.CompletionRate);
    }

    [Fact]
    public void ComputeStats_IgnoresCanceledTasks()
    {
        var tasks = new[]
        {
            Task("取消的", TaskStatus.Canceled, due: "2026-11-05T18:00")
        };

        var stats = ReportComposer.ComputeStats(ReportPeriods.Weekly(At(11, 8)), tasks, At(11, 8));

        Assert.Equal(0, stats.Completed);
        Assert.Equal(0, stats.CarriedOver);
        Assert.Equal(0, stats.Overdue);
    }

    [Fact]
    public void ComputeStats_IgnoresTasksWithoutDueDate()
    {
        var tasks = new[] { Task("没期限", TaskStatus.Todo) };

        var stats = ReportComposer.ComputeStats(ReportPeriods.Weekly(At(11, 8)), tasks, At(11, 8));

        Assert.Equal(0, stats.CarriedOver);
    }

    [Fact]
    public void ComputeStats_CompletedCountsByCompletionTimeNotDueTime()
    {
        // 本周到期、本周三做完：算本周的完成
        var thisWeek = new[] { Task("本周做完的", TaskStatus.Done, due: "2026-11-03T18:00", completed: "2026-11-04T02:00:00.000Z") };

        var stats = ReportComposer.ComputeStats(ReportPeriods.Weekly(At(11, 8)), thisWeek, At(11, 8));

        Assert.Equal(1, stats.Completed);
        Assert.Equal(0, stats.CarriedOver);
    }

    [Fact]
    public void ComputeStats_MonthlyDoesNotCountFutureDueAsOverdue()
    {
        // 月报到 11-30，11-25 到期的任务在 11-08 还不该算逾期
        var tasks = new[] { Task("月底才到期", TaskStatus.Todo, due: "2026-11-25T18:00") };

        var stats = ReportComposer.ComputeStats(ReportPeriods.Monthly(At(11, 8)), tasks, At(11, 8));

        Assert.Equal(1, stats.CarriedOver);
        Assert.Equal(0, stats.Overdue);
    }

    [Fact]
    public void Compose_WritesHeaderStatsAndThreeSections()
    {
        var tasks = new[]
        {
            Task("已做完", TaskStatus.Done, completed: "2026-11-05T02:00:00.000Z", projectId: "p1"),
            Task("还欠着", TaskStatus.Doing, due: "2026-11-05T18:00", projectId: "p1"),
            Task("早就过期了", TaskStatus.Todo, due: "2026-11-03T18:00")
        };

        var markdown = ReportComposer.Compose(ReportPeriods.Weekly(At(11, 5)), tasks, [Project], At(11, 5));

        Assert.Equal("""
            ## 周报 2026-11-02 ~ 2026-11-08

            ### 统计
            - 完成 1 项 · 遗留未完成 2 项（其中逾期 1 项）· 完成率 33%

            ### 已完成
            - [官网改版] 已做完

            ### 遗留未完成
            - [官网改版] 还欠着（截止 11-05）

            ### 逾期
            - 早就过期了（截止 11-03，逾期 2 天）

            """.Replace("\r\n", "\n"), markdown); // 原始字符串的换行跟着源码文件走，Compose 的契约是固定 \n
    }

    [Fact]
    public void Compose_WritesNoneWhenSectionIsEmpty()
    {
        var markdown = ReportComposer.Compose(ReportPeriods.Weekly(At(11, 8)), [], [], At(11, 8));

        Assert.Contains("### 已完成\n无\n", markdown);
        Assert.Contains("### 遗留未完成\n无\n", markdown);
        Assert.Contains("### 逾期\n无\n", markdown);
    }

    [Fact]
    public void ComputeStats_IgnoresTasksCompletedOutsideThePeriod()
    {
        // 上周做完的，不算本周的完成 —— 完成按完成时间落窗
        var tasks = new[]
        {
            Task("上周做完的", TaskStatus.Done, completed: "2026-10-28T02:00:00.000Z")
        };

        var stats = ReportComposer.ComputeStats(ReportPeriods.Weekly(At(11, 5)), tasks, At(11, 5));

        Assert.Equal(0, stats.Completed);
        Assert.Equal(0, stats.Total);
    }

    [Fact]
    public void ComputeStats_IgnoresTasksDueOutsideThePeriod()
    {
        // 下周才到期的不算本周的遗留 —— 遗留按到期时间落窗
        var tasks = new[]
        {
            Task("下周才到期", TaskStatus.Doing, due: "2026-11-10T18:00")
        };

        var stats = ReportComposer.ComputeStats(ReportPeriods.Weekly(At(11, 5)), tasks, At(11, 5));

        Assert.Equal(0, stats.CarriedOver);
        Assert.Equal(0, stats.Overdue);
    }

    [Fact]
    public void Merge_JoinsAutoAndManualText()
    {
        Assert.Equal("自动", ReportComposer.Merge("自动", ""));
        Assert.Equal("人工", ReportComposer.Merge("", "人工"));
        Assert.Equal("自动\n\n人工", ReportComposer.Merge("自动", "人工"));
    }
}
