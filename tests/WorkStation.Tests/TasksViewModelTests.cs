using Microsoft.Data.Sqlite;
using WorkStation.App.ViewModels;
using WorkStation.Core.Models;
using WorkStation.Core.Services;

namespace WorkStation.Tests;

/// <summary>任务页视图模型：分组、快速添加、完成、筛选、折叠。</summary>
public class TasksViewModelTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly WorkStationService _service;
    private readonly List<(string Message, bool IsError)> _toasts = [];

    // 固定时钟：2026-11-05 是周四，本周结束于 11-08（周日）
    private static readonly DateTime Now = new(2026, 11, 5, 10, 0, 0, DateTimeKind.Local);

    public TasksViewModelTests()
    {
        _connection = TestDb.OpenMigrated();
        _service = new WorkStationService(_connection, ":memory:");
    }

    public void Dispose() => _connection.Dispose();

    private TasksViewModel CreateVm()
    {
        var vm = new TasksViewModel(_service, (message, isError) => _toasts.Add((message, isError)), () => Now);
        vm.Reload();
        return vm;
    }

    [Fact]
    public void Reload_Empty_ShowsEmptyState()
    {
        var vm = CreateVm();

        Assert.Empty(vm.Groups);
        Assert.True(vm.ShowEmpty);
        Assert.False(vm.HasError);
    }

    [Fact]
    public void QuickAdd_CreatesTaskAndClearsInput()
    {
        var vm = CreateVm();
        vm.QuickAddTitle = "记一件事";

        vm.QuickAddCommand.Execute(null);

        Assert.Equal(string.Empty, vm.QuickAddTitle);
        Assert.Single(_service.TasksList().Data!);
        Assert.Single(vm.Groups);
        Assert.Equal("无期限", vm.Groups[0].Title);
    }

    [Fact]
    public void QuickAdd_BlankInput_DoesNothing()
    {
        var vm = CreateVm();
        vm.QuickAddTitle = "   ";

        vm.QuickAddCommand.Execute(null);

        Assert.Empty(_service.TasksList().Data!);
    }

    [Fact]
    public void Groups_FollowDesignOrder()
    {
        _service.TasksCreate(new CreateTaskInput { Title = "以后", DueAt = "2026-12-01T10:00" });
        _service.TasksCreate(new CreateTaskInput { Title = "本周", DueAt = "2026-11-07T10:00" });
        _service.TasksCreate(new CreateTaskInput { Title = "今日", DueAt = "2026-11-05T18:00" });
        _service.TasksCreate(new CreateTaskInput { Title = "逾期", DueAt = "2026-11-04T10:00" });
        _service.TasksCreate(new CreateTaskInput { Title = "无期限" });

        var vm = CreateVm();

        Assert.Equal(
            ["今日", "逾期", "本周", "以后", "无期限"],
            vm.Groups.Select(g => g.Title));
    }

    [Fact]
    public void Groups_FinishedTasksGoToFinishedGroup()
    {
        _service.TasksCreate(new CreateTaskInput { Title = "完成", Status = TaskStatus.Done, DueAt = "2026-11-05T09:00" });
        _service.TasksCreate(new CreateTaskInput { Title = "取消", Status = TaskStatus.Canceled });

        var vm = CreateVm();

        Assert.Single(vm.Groups);
        Assert.Equal("已完结", vm.Groups[0].Title);
        Assert.Equal(2, vm.Groups[0].Tasks.Count);
    }

    [Fact]
    public void Complete_MovesTaskToFinishedGroup()
    {
        var task = _service.TasksCreate(new CreateTaskInput { Title = "勾掉我" }).Data!;
        var vm = CreateVm();
        var row = vm.Groups.Single().Tasks.Single();

        row.CompleteCommand.Execute(null);

        Assert.Equal(TaskStatus.Done, _service.TasksGet(task.Id).Data!.Status);
        Assert.Equal("已完结", vm.Groups.Single().Title);
    }

    [Fact]
    public void ToggleGroup_CollapsesAndSurvivesReload()
    {
        _service.TasksCreate(new CreateTaskInput { Title = "一条" });
        var vm = CreateVm();
        var group = vm.Groups.Single();

        group.ToggleCommand.Execute(null);

        Assert.False(group.IsExpanded);

        vm.Reload();

        Assert.False(vm.Groups.Single().IsExpanded);
    }

    [Fact]
    public void StatusFilter_LimitsRows()
    {
        _service.TasksCreate(new CreateTaskInput { Title = "待办" });
        _service.TasksCreate(new CreateTaskInput { Title = "进行中", Status = TaskStatus.Doing });

        var vm = CreateVm();
        vm.SelectedStatusFilter = vm.StatusFilters.Single(f => f.Key == "doing");

        Assert.Single(vm.Groups.Single().Tasks);
        Assert.Equal("进行中", vm.Groups.Single().Tasks[0].Title);
    }

    [Fact]
    public void Search_FiltersByKeyword()
    {
        _service.TasksCreate(new CreateTaskInput { Title = "重构数据库" });
        _service.TasksCreate(new CreateTaskInput { Title = "买牛奶" });

        var vm = CreateVm();
        vm.SearchText = "重构";

        Assert.Single(vm.Groups.Single().Tasks);
        Assert.Equal("重构数据库", vm.Groups.Single().Tasks[0].Title);
    }

    [Fact]
    public void ClearFilters_RestoresEverything()
    {
        _service.TasksCreate(new CreateTaskInput { Title = "一条" });
        var vm = CreateVm();
        vm.SearchText = "不存在的关键字";

        Assert.True(vm.ShowEmpty);

        vm.ClearFiltersCommand.Execute(null);

        Assert.False(vm.ShowEmpty);
        Assert.Single(vm.Groups.Single().Tasks);
    }

    [Fact]
    public void TaskForm_PickDateOnly_GetsDefaultHour()
    {
        var vm = CreateVm();
        vm.NewTaskCommand.Execute(null);
        vm.Form!.Title = "只选日期";
        vm.Form.DueDate = new DateTime(2026, 11, 6);

        // 只挑日期时，时间选择器里要直接显示默认的 18:00，而不是留空让人猜
        Assert.Equal(new TimeSpan(18, 0, 0), vm.Form.DueTime);

        Assert.True(vm.Form.Validate());
        vm.SaveFormCommand.Execute(null);

        var task = _service.TasksList().Data!.Single();
        Assert.Equal("2026-11-06T18:00", task.DueAt);
    }

    [Fact]
    public void TaskForm_PickTimeWithoutDate_IsRejected()
    {
        var vm = CreateVm();
        vm.NewTaskCommand.Execute(null);
        vm.Form!.Title = "只有时刻";
        vm.Form.DueTime = new TimeSpan(9, 30, 0);

        Assert.False(vm.Form.Validate());
        Assert.Contains("选日期", vm.Form.Error);
    }

    [Fact]
    public void TaskForm_ClearDue_RemovesDueAt()
    {
        var task = _service.TasksCreate(new CreateTaskInput { Title = "要清空的", DueAt = "2026-11-06T18:00" }).Data!;
        var vm = CreateVm();

        vm.Groups.Single().Tasks.Single().EditCommand.Execute(null);
        Assert.Equal(new DateTime(2026, 11, 6), vm.Form!.DueDate);
        Assert.Equal(new TimeSpan(18, 0, 0), vm.Form.DueTime);

        vm.Form.ClearDueCommand.Execute(null);
        vm.SaveFormCommand.Execute(null);

        Assert.Null(_service.TasksGet(task.Id).Data!.DueAt);
    }

    [Fact]
    public void Delete_RequiresConfirmation()
    {
        var task = _service.TasksCreate(new CreateTaskInput { Title = "要删的" }).Data!;
        var vm = CreateVm();

        vm.Groups.Single().Tasks.Single().DeleteCommand.Execute(null);
        Assert.True(vm.IsDeleteConfirmOpen);
        Assert.NotNull(_service.TasksGet(task.Id).Data);

        vm.ConfirmDeleteCommand.Execute(null);

        Assert.False(vm.IsDeleteConfirmOpen);
        Assert.Null(_service.TasksGet(task.Id).Data);
    }

    [Fact]
    public void StatusChip_TogglesSelectionFlags()
    {
        _service.TasksCreate(new CreateTaskInput { Title = "做着的", Status = TaskStatus.Doing });
        _service.TasksCreate(new CreateTaskInput { Title = "没动的" });
        var vm = CreateVm();

        // 初始只有「全部」是高亮的，否则一排胶囊全是选中态就没法看
        Assert.Single(vm.StatusFilters, f => f.IsSelected);
        Assert.True(vm.StatusFilters[0].IsSelected);

        var doing = vm.StatusFilters.Single(f => f.Key == "doing");
        doing.SelectCommand.Execute(null);

        Assert.Same(doing, vm.SelectedStatusFilter);
        Assert.True(doing.IsSelected);
        Assert.False(vm.StatusFilters[0].IsSelected);
        Assert.Equal(["做着的"], vm.Groups.SelectMany(g => g.Tasks).Select(r => r.Title).ToArray());
    }

    [Fact]
    public void Row_MetaText_JoinsProjectPriorityAndStatus()
    {
        var project = _service.ProjectsCreate(new CreateProjectInput { Name = "工作台" }).Data!;
        _service.TasksCreate(new CreateTaskInput
        {
            Title = "有项目有优先级",
            ProjectId = project.Id,
            Priority = Priority.High,
        });
        _service.TasksCreate(new CreateTaskInput { Title = "什么都没设", Priority = Priority.None });

        var vm = CreateVm();
        var rows = vm.Groups.SelectMany(g => g.Tasks).ToDictionary(r => r.Title);

        Assert.Equal("工作台 · 高优先级", rows["有项目有优先级"].MetaText);

        // 三项都为空时整行隐藏，避免留下孤零零的分隔符或高度
        Assert.False(rows["什么都没设"].HasMeta);
    }
}
