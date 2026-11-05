using Microsoft.Data.Sqlite;
using WorkStation.App.ViewModels;
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
    public void BlockedChip_IsIndependentFromTimeScope()
    {
        _service.TasksCreate(new CreateTaskInput { Title = "阻塞的", Status = TaskStatus.Blocked, DueAt = "2026-11-04T09:00" });
        _service.TasksCreate(new CreateTaskInput { Title = "待办" });

        var vm = CreateVm();
        vm.OverviewChips.Single(c => c.Key == "blocked").SelectCommand.Execute(null);

        Assert.True(vm.OnlyBlocked);
        Assert.Equal(["阻塞的"], vm.Columns.SelectMany(c => c.Cards).Select(c => c.Title).ToArray());
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
}
