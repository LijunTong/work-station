using Microsoft.Data.Sqlite;
using WorkStation.App.ViewModels;
using WorkStation.Core.Board;
using WorkStation.Core.Models;
using WorkStation.Core.Services;

namespace WorkStation.Tests;

/// <summary>看板视图模型：分列、概览筛选、拖拽改状态。</summary>
public class BoardViewModelTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly WorkStationService _service;
    private readonly List<(string Message, bool IsError)> _toasts = [];

    // 固定时钟：2026-11-05 是周四，本周结束于 11-08（周日）
    private static readonly DateTime Now = new(2026, 11, 5, 10, 0, 0, DateTimeKind.Local);

    public BoardViewModelTests()
    {
        _connection = TestDb.OpenMigrated();
        _service = new WorkStationService(_connection, ":memory:");
    }

    public void Dispose() => _connection.Dispose();

    private BoardViewModel CreateVm()
    {
        var vm = new BoardViewModel(
            _service,
            (message, isError) => _toasts.Add((message, isError)),
            () => Now);

        vm.Reload();
        return vm;
    }

    private static string[] Titles(BoardColumnViewModel column) =>
        column.Cards.Select(c => c.Title).ToArray();

    [Fact]
    public void Reload_BuildsFourColumns()
    {
        _service.TasksCreate(new CreateTaskInput { Title = "待办" });
        _service.TasksCreate(new CreateTaskInput { Title = "做着的", Status = TaskStatus.Doing });
        _service.TasksCreate(new CreateTaskInput { Title = "卡住的", Status = TaskStatus.Blocked });
        _service.TasksCreate(new CreateTaskInput { Title = "完成的", Status = TaskStatus.Done });

        var vm = CreateVm();

        Assert.Equal(["待办", "进行中", "已阻塞", "已完成"], vm.Columns.Select(c => c.Title).ToArray());
        Assert.Equal([1, 1, 1, 1], vm.Columns.Select(c => c.Count).ToArray());
    }

    [Fact]
    public void CanceledTask_NeverAppearsOnBoard()
    {
        _service.TasksCreate(new CreateTaskInput { Title = "取消", Status = TaskStatus.Canceled });
        _service.TasksCreate(new CreateTaskInput { Title = "正常" });

        var vm = CreateVm();

        Assert.Equal(["正常"], vm.Columns.SelectMany(c => c.Cards).Select(c => c.Title).ToArray());
    }

    [Fact]
    public void DoneColumn_HidesOldCompletionsUntilWidened()
    {
        var old = _service.TasksCreate(new CreateTaskInput { Title = "上个月的", Status = TaskStatus.Done }).Data!;
        _service.TasksCreate(new CreateTaskInput { Title = "刚完成", Status = TaskStatus.Done });
        TestDb.Execute(
            _connection,
            $"UPDATE tasks SET completed_at = '2026-08-01T00:00:00.000Z' WHERE id = '{old.Id}'");

        var vm = CreateVm();
        var done = vm.Columns.Single(c => c.Status == TaskStatus.Done);

        Assert.Equal(["刚完成"], Titles(done));
        Assert.Equal("显示全部", done.DoneWindowLabel);

        vm.ToggleDoneWindowCommand.Execute(null);

        // 切换窗口会重排整块看板，必须重新取列对象
        var widened = vm.Columns.Single(c => c.Status == TaskStatus.Done);
        Assert.Equal(2, widened.Count);
        Assert.Equal("只看近 7 天", widened.DoneWindowLabel);
    }

    [Fact]
    public void OverviewChip_FiltersAndTogglesBack()
    {
        _service.TasksCreate(new CreateTaskInput { Title = "今日", DueAt = "2026-11-05T18:00" });
        _service.TasksCreate(new CreateTaskInput { Title = "逾期", DueAt = "2026-11-04T09:00" });
        _service.TasksCreate(new CreateTaskInput { Title = "以后", DueAt = "2026-12-01T09:00" });

        var vm = CreateVm();
        var today = vm.OverviewChips.Single(c => c.Key == "today");

        Assert.Equal(1, today.Count);
        Assert.Equal(1, vm.OverviewChips.Single(c => c.Key == "overdue").Count);

        today.SelectCommand.Execute(null);

        Assert.True(today.IsSelected);
        Assert.Equal(
            ["今日"],
            vm.Columns.SelectMany(c => c.Cards).Select(c => c.Title).ToArray());

        // 再点一次回到全部
        today.SelectCommand.Execute(null);

        Assert.False(today.IsSelected);
        Assert.Equal(3, vm.Columns.SelectMany(c => c.Cards).Count());
    }

    [Fact]
    public void BlockedFilter_ThroughDashboardEntry_KeepsWorking()
    {
        _service.TasksCreate(new CreateTaskInput { Title = "阻塞的", Status = TaskStatus.Blocked, DueAt = "2026-11-04T09:00" });
        _service.TasksCreate(new CreateTaskInput { Title = "待办" });

        var vm = CreateVm();
        // 概览条上不再有「已阻塞」胶囊，但仪表盘仍可以跳到这个口径
        Assert.DoesNotContain(vm.OverviewChips, c => c.Key == "blocked");
        vm.ShowBlockedOnly();

        Assert.True(vm.OnlyBlocked);
        Assert.Equal(["阻塞的"], vm.Columns.SelectMany(c => c.Cards).Select(c => c.Title).ToArray());
    }

    [Fact]
    public void PriorityChip_ToggleButton_KeepsSingleSelection()
    {
        var vm = CreateVm();
        var all = vm.PriorityChips.Single(c => c.Key == "all");
        var high = vm.PriorityChips.Single(c => c.Key == "high");

        // ToggleButton 点选：先翻转 IsSelected，再走命令
        high.IsSelected = true;
        high.SelectCommand.Execute(null);

        Assert.Same(high, vm.SelectedPriority);
        Assert.True(high.IsSelected);
        Assert.False(all.IsSelected);

        // 取消勾选唯一选中项会被复位（模拟 ToggleButton 先置 false 再发命令）
        high.IsSelected = false;
        high.SelectCommand.Execute(null);

        Assert.Same(high, vm.SelectedPriority);
        Assert.True(high.IsSelected);
    }

    [Fact]
    public void Filters_ProjectPriorityAndSearch()
    {
        var project = _service.ProjectsCreate(new CreateProjectInput { Name = "工作台" }).Data!;
        _service.TasksCreate(new CreateTaskInput { Title = "重构数据库", ProjectId = project.Id, Priority = Priority.High });
        _service.TasksCreate(new CreateTaskInput { Title = "买牛奶" });

        var vm = CreateVm();

        vm.SelectedProject = vm.ProjectOptions.Single(o => o.Id == project.Id);
        Assert.Equal(["重构数据库"], vm.Columns.SelectMany(c => c.Cards).Select(c => c.Title).ToArray());

        vm.ClearFiltersCommand.Execute(null);
        vm.PriorityChips.Single(c => c.Key == "high").SelectCommand.Execute(null);
        Assert.Equal(["重构数据库"], vm.Columns.SelectMany(c => c.Cards).Select(c => c.Title).ToArray());

        vm.ClearFiltersCommand.Execute(null);
        vm.SearchText = "牛奶";
        Assert.Equal(["买牛奶"], vm.Columns.SelectMany(c => c.Cards).Select(c => c.Title).ToArray());

        Assert.True(vm.HasFilter);
        vm.ClearFiltersCommand.Execute(null);
        Assert.False(vm.HasFilter);
    }

    [Fact]
    public void MoveTask_WritesStatusAndTimestamps()
    {
        var task = _service.TasksCreate(new CreateTaskInput { Title = "拖我" }).Data!;
        var vm = CreateVm();

        Assert.True(vm.MoveTask(task.Id, TaskStatus.Doing));

        var doing = _service.TasksGet(task.Id).Data!;
        Assert.Equal(TaskStatus.Doing, doing.Status);
        Assert.NotNull(doing.StartedAt);
        Assert.Null(doing.CompletedAt);

        Assert.True(vm.MoveTask(task.Id, TaskStatus.Done));

        var done = _service.TasksGet(task.Id).Data!;
        Assert.Equal(TaskStatus.Done, done.Status);
        Assert.NotNull(done.CompletedAt);

        // 从「已完成」拖回去要清掉完成时间，否则周报会把撤回的任务算成已完成
        Assert.True(vm.MoveTask(task.Id, TaskStatus.Todo));
        Assert.Null(_service.TasksGet(task.Id).Data!.CompletedAt);
    }

    [Fact]
    public void MoveTask_ToSameColumn_DoesNothing()
    {
        var task = _service.TasksCreate(new CreateTaskInput { Title = "没动" }).Data!;
        var vm = CreateVm();
        var before = _service.TasksGet(task.Id).Data!;

        Assert.False(vm.MoveTask(task.Id, TaskStatus.Todo));

        Assert.Equal(before.UpdatedAt, _service.TasksGet(task.Id).Data!.UpdatedAt);
        Assert.Empty(_toasts);
    }

    [Fact]
    public void MoveTask_UnknownId_ReportsError()
    {
        var vm = CreateVm();

        Assert.False(vm.MoveTask("不存在", TaskStatus.Doing));
        Assert.Contains(_toasts, t => t.IsError);
    }

    [Fact]
    public void MoveProject_ChangesStatusColumn()
    {
        var project = _service.ProjectsCreate(new CreateProjectInput { Name = "工作台" }).Data!;
        var vm = CreateVm();

        Assert.Equal(["工作台"], vm.ProjectColumns.Single(c => c.Status == ProjectStatus.Planning)
            .Cards.Select(c => c.Name).ToArray());

        Assert.True(vm.MoveProject(project.Id, ProjectStatus.Active));

        Assert.Equal(ProjectStatus.Active, _service.ProjectsGet(project.Id).Data!.Status);
        Assert.Equal(["工作台"], vm.ProjectColumns.Single(c => c.Status == ProjectStatus.Active)
            .Cards.Select(c => c.Name).ToArray());
        Assert.Empty(vm.ProjectColumns.Single(c => c.Status == ProjectStatus.Planning).Cards);
    }

    [Fact]
    public void ProjectCard_CarriesProgressAndCounts()
    {
        var project = _service.ProjectsCreate(new CreateProjectInput { Name = "工作台" }).Data!;
        _service.TasksCreate(new CreateTaskInput { Title = "完成", ProjectId = project.Id, Status = TaskStatus.Done });
        _service.TasksCreate(new CreateTaskInput { Title = "进行中", ProjectId = project.Id, Status = TaskStatus.Doing });

        var vm = CreateVm();
        var card = vm.ProjectColumns.SelectMany(c => c.Cards).Single();

        Assert.Equal(50, card.Progress);
        Assert.Equal(2, card.Counts.Total);
        Assert.Equal(1, card.Counts.Doing);
    }

    [Fact]
    public void SwitchBoard_TogglesMode()
    {
        var vm = CreateVm();

        Assert.True(vm.IsTaskBoard);
        Assert.False(vm.IsProjectBoard);

        vm.SwitchBoardCommand.Execute(nameof(BoardMode.Projects));

        Assert.False(vm.IsTaskBoard);
        Assert.True(vm.IsProjectBoard);
    }

    [Fact]
    public void MoveTask_NotifiesDataChangedForBadgeRefresh()
    {
        var task = _service.TasksCreate(new CreateTaskInput { Title = "拖我" }).Data!;
        var changes = 0;
        var vm = new BoardViewModel(_service, (_, _) => { }, () => Now, () => changes++);
        vm.Reload();

        vm.MoveTask(task.Id, TaskStatus.Done);

        Assert.Equal(1, changes);
    }

    [Fact]
    public void ShowToday_AppliesTodayScope()
    {
        _service.TasksCreate(new CreateTaskInput
        {
            Title = "今天到期",
            DueAt = Core.Time.LocalTime.ToLocalDateTime(Now.Date.AddHours(18))
        });
        _service.TasksCreate(new CreateTaskInput
        {
            Title = "下周到期",
            DueAt = Core.Time.LocalTime.ToLocalDateTime(Now.Date.AddDays(5).AddHours(18))
        });

        var vm = CreateVm();
        vm.ShowToday();

        Assert.Equal(BoardTimeScope.Today, vm.TimeScope);
        Assert.True(vm.OverviewChips[0].IsSelected);
        Assert.Contains(vm.Columns.SelectMany(c => c.Cards), c => c.Title == "今天到期");
        Assert.DoesNotContain(vm.Columns.SelectMany(c => c.Cards), c => c.Title == "下周到期");
    }

    /// <summary>
    /// 看板列内按优先级排序：同一列里高优先级的卡片排在最前面。
    /// </summary>
    [Fact]
    public void Columns_SortCardsByPriority()
    {
        // 全部同一天到期、都是待办 → 都在「待办」列，只差优先级
        var due = Core.Time.LocalTime.ToLocalDateTime(Now.Date.AddHours(18));
        _service.TasksCreate(new CreateTaskInput { Title = "低的", Priority = Priority.Low, DueAt = due });
        _service.TasksCreate(new CreateTaskInput { Title = "高的", Priority = Priority.High, DueAt = due });
        _service.TasksCreate(new CreateTaskInput { Title = "中的", Priority = Priority.Medium, DueAt = due });
        _service.TasksCreate(new CreateTaskInput { Title = "无优先级的", Priority = Priority.None, DueAt = due });

        var vm = CreateVm();
        var todo = vm.Columns.Single(c => c.Title == "待办");

        Assert.Equal(["高的", "中的", "低的", "无优先级的"], Titles(todo));
    }

    /// <summary>看板的项目卡也按优先级排：高的项目在前面。</summary>
    [Fact]
    public void ProjectColumns_SortByPriority()
    {
        var low = _service.ProjectsCreate(new CreateProjectInput { Name = "低的项目", Priority = Priority.Low }).Data!;
        var high = _service.ProjectsCreate(new CreateProjectInput { Name = "高的项目", Priority = Priority.High }).Data!;
        var none = _service.ProjectsCreate(new CreateProjectInput { Name = "无优先级的", Priority = Priority.None }).Data!;

        var vm = CreateVm();

        // 切到项目看板（不是任务看板）
        vm.IsTaskBoard = false;
        vm.Reload();

        var names = vm.ProjectColumns.SelectMany(c => c.Cards).Select(c => c.Name).ToArray();
        Assert.Equal(["高的项目", "低的项目", "无优先级的"], names);
        Assert.Contains(high.Id, vm.ProjectColumns.SelectMany(c => c.Cards).Select(c => c.Project.Id));
        Assert.Contains(low.Id, vm.ProjectColumns.SelectMany(c => c.Cards).Select(c => c.Project.Id));
        Assert.Contains(none.Id, vm.ProjectColumns.SelectMany(c => c.Cards).Select(c => c.Project.Id));
    }

    [Fact]
    public void ReminderChip_RemovedFromOverview()
    {
        var vm = CreateVm();

        // 「提醒」不是看板上的筛选，概览条上不再有它（跳提醒页走左侧菜单）
        Assert.DoesNotContain(vm.OverviewChips, c => c.Key == "reminders");

        // 顺序也钉死：今日 / 本周 / 本月 是三个时间口径，逾期是异常集合，排最后
        Assert.Equal(
            ["today", "week", "month", "overdue"],
            vm.OverviewChips.Select(c => c.Key).ToArray());
    }

    /// <summary>
    /// 逾期在看板顶部有一条提示：写清有几条、最早哪天，点一下切到逾期口径。
    /// 已经在看逾期口径时不再提示（别在同一件事上反复催）。
    /// </summary>
    [Fact]
    public void OverdueHint_CountsAndSwitchesScope()
    {
        _service.TasksCreate(new CreateTaskInput
        {
            Title = "早该做了",
            // 注意用 Now.Date 起步：Now 是 10 点，直接 AddHours(18) 会滚到第二天
            DueAt = Core.Time.LocalTime.ToLocalDateTime(Now.Date.AddDays(-3).AddHours(18))
        });

        var vm = CreateVm();

        Assert.Equal(1, vm.OverdueCount);
        Assert.True(vm.ShowOverdueHint);
        Assert.Equal("11-02", vm.EarliestOverdueText);
        Assert.Contains("已逾期", vm.OverdueHintText);

        vm.ShowOverdueOnlyCommand.Execute(null);

        Assert.Equal(BoardTimeScope.Overdue, vm.TimeScope);
        Assert.False(vm.ShowOverdueHint);
    }

    /// <summary>概览条新增「本月」：数字与筛选口径一致。</summary>
    [Fact]
    public void MonthChip_FiltersTasksDueThisMonth()
    {
        var now = new DateTime(2026, 11, 5, 10, 0, 0, DateTimeKind.Local);
        _service.TasksCreate(new CreateTaskInput
        {
            Title = "本月到期",
            DueAt = Core.Time.LocalTime.ToLocalDateTime(now.Date.AddDays(10).AddHours(18))
        });
        _service.TasksCreate(new CreateTaskInput
        {
            Title = "下月到期",
            DueAt = Core.Time.LocalTime.ToLocalDateTime(now.Date.AddDays(40).AddHours(18))
        });

        var vm = CreateVm();
        var month = vm.OverviewChips.Single(c => c.Key == "month");
        Assert.Equal(1, month.Count);

        month.SelectCommand.Execute(null);

        Assert.Equal(BoardTimeScope.Month, vm.TimeScope);
        Assert.True(month.IsSelected);
        Assert.Equal(
            ["本月到期"],
            vm.Columns.SelectMany(c => c.Cards).Select(c => c.Title).ToArray());
    }
}
