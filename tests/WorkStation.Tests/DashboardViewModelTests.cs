using Microsoft.Data.Sqlite;
using WorkStation.App.ViewModels;
using WorkStation.Core.Dashboard;
using WorkStation.Core.Models;
using WorkStation.Core.Services;

namespace WorkStation.Tests;

/// <summary>
/// 仪表盘：核心统计口径 + 视图模型 + 跳转。
///
/// 统计口径必须与看板一致 —— 用户在仪表盘看到「今日 3」，点去看板也得是 3，
/// 所以这里对着同一个任务集合同时断言两边。
/// </summary>
public class DashboardViewModelTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly WorkStationService _service;
    private readonly List<(string Message, bool IsError)> _toasts = [];

    // 固定时钟：2026-11-05 是周四，本周为 11-02（周一）至 11-08（周日）
    private static readonly DateTime Now = new(2026, 11, 5, 10, 0, 0, DateTimeKind.Local);

    public DashboardViewModelTests()
    {
        _connection = TestDb.OpenMigrated();
        _service = new WorkStationService(_connection, ":memory:");
    }

    public void Dispose() => _connection.Dispose();

    private DashboardViewModel CreateVm()
    {
        var vm = new DashboardViewModel(
            _service,
            (message, isError) => _toasts.Add((message, isError)),
            () => Now);

        vm.Reload();
        return vm;
    }

    /// <summary>构造任务对象：完成时间要可控，纯函数测试不能依赖 DB 的真实时钟。</summary>
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
        value.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss.fffZ", System.Globalization.CultureInfo.InvariantCulture);

    [Fact]
    public void ComputeRanges_CountsTodayWeekMonth()
    {
        // 今天到期
        _service.TasksCreate(new CreateTaskInput
        {
            Title = "今天到期",
            DueAt = Core.Time.LocalTime.ToLocalDateTime(Now.Date.AddHours(18))
        });
        // 本周三到期（周内）
        _service.TasksCreate(new CreateTaskInput
        {
            Title = "本周到期",
            DueAt = Core.Time.LocalTime.ToLocalDateTime(Now.Date.AddDays(2).AddHours(18))
        });
        // 下月到期：不进本周，也不进本月
        _service.TasksCreate(new CreateTaskInput
        {
            Title = "下月到期",
            DueAt = Core.Time.LocalTime.ToLocalDateTime(Now.Date.AddMonths(1).AddDays(1).AddHours(18))
        });

        var tasks = _service.TasksList().Data!;
        var ranges = DashboardStats.ComputeRanges(tasks, Now);

        Assert.Equal(1, ranges[DashboardRange.Today].Due);
        Assert.Equal(2, ranges[DashboardRange.Week].Due);
        Assert.Equal(2, ranges[DashboardRange.Month].Due);
    }

    [Fact]
    public void ComputeRanges_CountsCompletedByCompletionTime()
    {
        // 完成时间是真实时钟写入的，直接构造 TaskItem 才能控制落窗
        var tasks = new[]
        {
            Task("今天做完的", TaskStatus.Done, dueAt: "2026-10-30T18:00", completedAt: Iso(Now)),
            Task("昨天做完的", TaskStatus.Done, dueAt: "2026-10-30T18:00", completedAt: Iso(Now.AddDays(-1))),
            Task("上月做完的", TaskStatus.Done, dueAt: "2026-10-30T18:00", completedAt: Iso(Now.AddDays(-20)))
        };

        var ranges = DashboardStats.ComputeRanges(tasks, Now);

        // 原定截止在上个月，但完成时间是今天 → 算「今日完成」
        Assert.Equal(1, ranges[DashboardRange.Today].Completed);
        Assert.Equal(2, ranges[DashboardRange.Week].Completed);
        Assert.Equal(0, ranges[DashboardRange.Today].Due);
    }

    [Fact]
    public void ComputeRanges_AttentionMatchesBoardOverview()
    {
        _service.TasksCreate(new CreateTaskInput
        {
            Title = "今天到期",
            DueAt = Core.Time.LocalTime.ToLocalDateTime(Now.Date.AddHours(18))
        });
        _service.TasksCreate(new CreateTaskInput
        {
            Title = "昨天到期，已逾期",
            DueAt = Core.Time.LocalTime.ToLocalDateTime(Now.Date.AddDays(-1).AddHours(9))
        });

        var tasks = _service.TasksList().Data!;
        var (overdue, blocked) = DashboardStats.ComputeAttention(tasks, Now);
        var board = Core.Board.TaskBoard.ComputeOverview(tasks, Now);

        // 逾期/已阻塞的口径必须与看板完全一致 —— 「去看板处理」跳过去数字要对得上
        Assert.Equal(board.Overdue, overdue);
        Assert.Equal(board.Blocked, blocked);
    }

    [Fact]
    public void TasksInRange_CountMatchesRangeStats()
    {
        _service.TasksCreate(new CreateTaskInput
        {
            Title = "今天到期",
            DueAt = Core.Time.LocalTime.ToLocalDateTime(Now.Date.AddHours(18))
        });
        _service.TasksCreate(new CreateTaskInput
        {
            Title = "下月到期",
            DueAt = Core.Time.LocalTime.ToLocalDateTime(Now.Date.AddMonths(1).AddDays(1).AddHours(18))
        });

        var tasks = _service.TasksList().Data!;
        var ranges = DashboardStats.ComputeRanges(tasks, Now);
        var todayTasks = DashboardStats.TasksInRange(tasks, DashboardRange.Today, Now);

        // 清单条数必须与卡片上的数字一致
        Assert.Equal(ranges[DashboardRange.Today].Due, todayTasks.Count);
        Assert.Equal("今天到期", todayTasks.Single().Title);
    }

    [Fact]
    public void ComputeTrend_ReturnsSevenDaysEndingToday()
    {
        var trend = DashboardStats.ComputeTrend(_service.TasksList().Data!, Now);

        Assert.Equal(7, trend.Count);
        Assert.True(trend[^1].IsToday);
        Assert.Equal(
            Core.Time.LocalTime.ToLocalDate(Now),
            trend[^1].Date);
    }

    [Fact]
    public void ComputeStatusBreakdown_SkipsCanceled()
    {
        _service.TasksCreate(new CreateTaskInput { Title = "待办" });
        _service.TasksCreate(new CreateTaskInput { Title = "取消", Status = TaskStatus.Canceled });

        var slices = DashboardStats.ComputeStatusBreakdown(_service.TasksList().Data!);

        Assert.Equal(4, slices.Count);
        Assert.Equal(1, slices.Single(s => s.Status == TaskStatus.Todo).Count);
        // 已取消不参与分布，总数只有 1
        Assert.Equal(1, slices.Sum(s => s.Count));
    }

    [Fact]
    public void ComputePriorityBreakdown_OnlyOpenTasks()
    {
        _service.TasksCreate(new CreateTaskInput { Title = "高优先", Priority = Priority.High });
        var done = _service.TasksCreate(new CreateTaskInput
        {
            Title = "做完的高优先",
            Priority = Priority.High
        }).Data!;
        _service.TasksSetStatus(done.Id, TaskStatus.Done);

        var slices = DashboardStats.ComputePriorityBreakdown(_service.TasksList().Data!);

        Assert.Equal(1, slices.Single(s => s.Priority == Priority.High).Count);
    }

    [Fact]
    public void Reload_FillsPeriodsAndBreakdowns()
    {
        _service.TasksCreate(new CreateTaskInput
        {
            Title = "今天到期",
            DueAt = Core.Time.LocalTime.ToLocalDateTime(Now.Date.AddHours(18))
        });
        _service.TasksCreate(new CreateTaskInput { Title = "没日期的" });

        var vm = CreateVm();

        Assert.Equal(3, vm.Periods.Count);
        Assert.Equal(1, vm.Periods.Single(p => p.Range == DashboardRange.Today).Due);
        Assert.Equal(7, vm.Trend.Count);
        Assert.Equal(4, vm.StatusBreakdown.Count);
        Assert.Equal(4, vm.PriorityBreakdown.Count);
        Assert.Equal(2, vm.TotalTasks);
        Assert.Equal(2, vm.OpenTasks);
    }

    [Fact]
    public void Reload_ReportsOverdueAndBlocked()
    {
        _service.TasksCreate(new CreateTaskInput
        {
            Title = "逾期的",
            DueAt = Core.Time.LocalTime.ToLocalDateTime(Now.Date.AddDays(-1).AddHours(9))
        });
        _service.TasksCreate(new CreateTaskInput { Title = "阻塞的", Status = TaskStatus.Blocked });

        var vm = CreateVm();

        Assert.Equal(1, vm.OverdueCount);
        Assert.Equal(1, vm.BlockedCount);
        Assert.True(vm.HasAttention);
        Assert.Contains("逾期 1", vm.AttentionText);
    }

    /// <summary>
    /// 今日/本周/本月的任务清单按优先级排序：高的在前，
    /// 同优先级再按截止时间早的在前。
    /// </summary>
    [Fact]
    public void TasksInRange_SortsByPriority()
    {
        // 全部同一天到期，只差优先级 —— 排序结果就只可能由优先级决定
        var due = Core.Time.LocalTime.ToLocalDateTime(Now.Date.AddHours(18));
        _service.TasksCreate(new CreateTaskInput { Title = "低的", Priority = Priority.Low, DueAt = due });
        _service.TasksCreate(new CreateTaskInput { Title = "高的", Priority = Priority.High, DueAt = due });
        _service.TasksCreate(new CreateTaskInput { Title = "中的", Priority = Priority.Medium, DueAt = due });
        _service.TasksCreate(new CreateTaskInput { Title = "无优先级的", Priority = Priority.None, DueAt = due });

        var vm = CreateVm();
        var today = vm.Periods.Single(p => p.Range == DashboardRange.Today);

        Assert.Equal(["高的", "中的", "低的", "无优先级的"], today.Tasks.Select(t => t.Title).ToArray());
    }

    /// <summary>本周与本月窗口用同一套排序口径。</summary>
    [Fact]
    public void TasksInRange_WeekAndMonth_AlsoSortByPriority()
    {
        var due = Core.Time.LocalTime.ToLocalDateTime(Now.Date.AddDays(2).AddHours(18));
        _service.TasksCreate(new CreateTaskInput { Title = "低的", Priority = Priority.Low, DueAt = due });
        _service.TasksCreate(new CreateTaskInput { Title = "高的", Priority = Priority.High, DueAt = due });

        var vm = CreateVm();

        foreach (var range in new[] { DashboardRange.Week, DashboardRange.Month })
        {
            var period = vm.Periods.Single(p => p.Range == range);
            Assert.Equal(["高的", "低的"], period.Tasks.Select(t => t.Title).ToArray());
        }
    }

    /// <summary>
    /// 「近期任务」清单里，同一天到期、同样紧急的任务，高优先级在前。
    /// </summary>
    [Fact]
    public void UpcomingTasks_SameUrgency_HighPriorityFirst()
    {
        var due = Core.Time.LocalTime.ToLocalDateTime(Now.Date.AddHours(18));
        _service.TasksCreate(new CreateTaskInput { Title = "无优先级的", Priority = Priority.None, DueAt = due });
        _service.TasksCreate(new CreateTaskInput { Title = "高的", Priority = Priority.High, DueAt = due });
        _service.TasksCreate(new CreateTaskInput { Title = "中的", Priority = Priority.Medium, DueAt = due });

        var vm = CreateVm();

        Assert.Equal(["高的", "中的", "无优先级的"], vm.UpcomingTasks.Select(t => t.Title).ToArray());
    }

    [Fact]
    public void SelectPeriod_SwitchesTaskList_MatchingTheCount()
    {
        _service.TasksCreate(new CreateTaskInput
        {
            Title = "今天到期",
            DueAt = Core.Time.LocalTime.ToLocalDateTime(Now.Date.AddHours(18))
        });

        var vm = CreateVm();
        var today = vm.Periods.Single(p => p.Range == DashboardRange.Today);
        var week = vm.Periods.Single(p => p.Range == DashboardRange.Week);

        // 默认选中「今日」：进仪表盘先看今天要扛多少活
        Assert.True(today.IsSelected);
        Assert.Same(today, vm.SelectedPeriod);

        // 卡片上的数字必须等于清单的条数
        Assert.Equal(today.Due, today.Tasks.Count);
        Assert.Equal("今天到期", today.Tasks.Single().Title);

        // 点「本周」：切换过去（同一时刻只有一张被选中）
        vm.SelectPeriodCommand.Execute(week);
        Assert.False(today.IsSelected);
        Assert.True(week.IsSelected);
        Assert.Same(week, vm.SelectedPeriod);

        // 再点一次**不会收起**：清单是常驻的，收起会在页面上空出一块
        vm.SelectPeriodCommand.Execute(week);
        Assert.True(week.IsSelected);
        Assert.Same(week, vm.SelectedPeriod);
        Assert.Equal(1, vm.Periods.Count(p => p.IsSelected));
    }

    [Fact]
    public void JumpToAttention_RaisesBoardTarget()
    {
        var vm = CreateVm();
        DashboardJumpTarget? target = null;
        vm.JumpToBoardRequested = t => target = t;

        vm.JumpToOverdueCommand.Execute(null);
        Assert.Equal(DashboardJumpTarget.Overdue, target);

        vm.JumpToBlockedCommand.Execute(null);
        Assert.Equal(DashboardJumpTarget.Blocked, target);
    }

    [Fact]
    public void ComputeProjectProgress_MatchesProjectPageCounts()
    {
        var project = _service.ProjectsCreate(new CreateProjectInput { Name = "官网改版" }).Data!;
        _service.TasksCreate(new CreateTaskInput { Title = "做首页", ProjectId = project.Id });
        _service.TasksCreate(new CreateTaskInput { Title = "做详情页", ProjectId = project.Id });

        var tasks = _service.TasksList().Data!;
        var projects = _service.ProjectsList().Data!;
        var progress = DashboardStats.ComputeProjectProgress(projects, tasks, Now).Single();

        // 仪表盘上的项目数字必须与项目页卡片一致 —— 同一份数据两处显示，
        // 数字对不上用户会当成 bug。直接对着 ProjectSummary 断言。
        var (counts, projectProgress) = Core.Projects.ProjectSummary.Compute(project, tasks, Now);

        Assert.Equal(project.Id, progress.Id);
        Assert.Equal("官网改版", progress.Name);
        Assert.Equal(counts.Total, progress.Total);
        Assert.Equal(counts.Done, progress.Done);
        Assert.Equal(counts.Doing, progress.Doing);
        Assert.Equal(counts.Overdue, progress.Overdue);
        Assert.Equal(projectProgress, progress.Progress);
        Assert.Equal("规划中", progress.StatusLabel);
    }

    [Fact]
    public void ComputeUpcomingTasks_PutsOverdueFirst()
    {
        var tasks = new[]
        {
            Task("还没到期", TaskStatus.Todo, dueAt: "2026-11-20T18:00"),
            Task("已经逾期", TaskStatus.Todo, dueAt: "2026-11-01T18:00"),
            Task("已完成的不该出现", TaskStatus.Done, dueAt: "2026-11-01T18:00", completedAt: Iso(Now))
        };

        var rows = DashboardStats.ComputeUpcomingTasks(tasks, new Dictionary<string, string>(), Now);

        // 逾期的排最前；已完成的不占清单位置
        Assert.Equal(2, rows.Count);
        Assert.Equal("已经逾期", rows[0].Title);
        Assert.True(rows[0].IsOverdue);
        Assert.False(rows[1].IsOverdue);
    }

    [Fact]
    public void ComputeUpcomingTasks_ResolvesProjectName()
    {
        var project = _service.ProjectsCreate(new CreateProjectInput { Name = "季度复盘" }).Data!;
        _service.TasksCreate(new CreateTaskInput { Title = "整理数据", ProjectId = project.Id });

        var tasks = _service.TasksList().Data!;
        var names = new Dictionary<string, string> { [project.Id] = "季度复盘" };

        var row = DashboardStats.ComputeUpcomingTasks(tasks, names, Now).Single();

        Assert.Equal("整理数据", row.Title);
        Assert.Equal("季度复盘", row.ProjectName);
    }

    [Fact]
    public void Reload_LoadsProjectsAndUpcomingTasks()
    {
        var project = _service.ProjectsCreate(new CreateProjectInput { Name = "官网改版" }).Data!;
        _service.TasksCreate(new CreateTaskInput
        {
            Title = "今天要做的事",
            ProjectId = project.Id,
            DueAt = Core.Time.LocalTime.ToLocalDateTime(Now.Date.AddHours(18))
        });

        var vm = CreateVm();

        Assert.True(vm.HasProjects);
        var card = vm.Projects.Single();
        Assert.Equal("官网改版", card.Name);
        Assert.Equal(1, card.Total);
        Assert.Equal("1 个任务 · 进行中 0", card.MetaText);

        Assert.True(vm.HasUpcomingTasks);
        Assert.Equal("今天要做的事", vm.UpcomingTasks.Single().Title);
        Assert.Equal("官网改版", vm.UpcomingTasks.Single().ProjectName);
        Assert.NotNull(vm.UpcomingTasks.Single().Source);
    }

    [Fact]
    public void ClickingProject_RequestsProjectTasks()
    {
        var project = _service.ProjectsCreate(new CreateProjectInput { Name = "官网改版" }).Data!;
        _service.TasksCreate(new CreateTaskInput { Title = "做首页", ProjectId = project.Id });

        var vm = CreateVm();
        string? opened = null;
        vm.OpenProjectRequested = id => opened = id;

        vm.OpenProjectCommand.Execute(vm.Projects.Single());

        Assert.Equal(project.Id, opened);
    }

    [Fact]
    public void ClickingTask_RequestsTaskEditor()
    {
        _service.TasksCreate(new CreateTaskInput { Title = "要改的任务" });

        var vm = CreateVm();
        TaskItem? opened = null;
        vm.OpenTaskRequested = t => opened = t;

        vm.OpenTaskCommand.Execute(vm.UpcomingTasks.Single());

        Assert.NotNull(opened);
        Assert.Equal("要改的任务", opened!.Title);
    }
}
