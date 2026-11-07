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
    public void OpenEditor_BuildsFormForTaskOutsideCurrentFilter()
    {
        var task = _service.TasksCreate(new CreateTaskInput { Title = "从看板点进来" }).Data!;
        var vm = CreateVm();
        vm.SearchText = "不存在的关键字";

        vm.OpenEditor(task);

        Assert.True(vm.IsFormOpen);
        Assert.Equal(task.Id, vm.Form!.TaskId);
        Assert.Equal("从看板点进来", vm.Form.Title);
    }

    [Fact]
    public void FocusProject_SetsFilterToThatProject()
    {
        var project = _service.ProjectsCreate(new CreateProjectInput { Name = "工作台" }).Data!;
        _service.TasksCreate(new CreateTaskInput { Title = "项目内任务", ProjectId = project.Id });
        _service.TasksCreate(new CreateTaskInput { Title = "独立任务" });

        var vm = CreateVm();
        vm.FocusProject(project.Id);

        Assert.Equal(project.Id, vm.SelectedProject!.Id);
        Assert.Equal(
            ["项目内任务"],
            vm.Groups.SelectMany(g => g.Tasks).Select(r => r.Title).ToArray());
    }

    [Fact]
    public void FocusProject_UnknownId_KeepsCurrentFilter()
    {
        var vm = CreateVm();
        var before = vm.SelectedProject;

        vm.FocusProject("不存在的项目");

        Assert.Same(before, vm.SelectedProject);
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
        // 两条都是已完结，默认「待办」会把它们过滤掉；切到「全部」才看得到「已完结」分组
        vm.SelectedStatusFilter = vm.StatusFilters.Single(f => f.Key == "all");

        Assert.Single(vm.Groups);
        Assert.Equal("已完结", vm.Groups[0].Title);
        Assert.Equal(2, vm.Groups[0].Tasks.Count);
    }

    [Fact]
    public void Complete_MovesTaskToFinishedGroup()
    {
        var task = _service.TasksCreate(new CreateTaskInput { Title = "勾掉我" }).Data!;
        var vm = CreateVm();
        // 切到「全部」：勾掉后任务进「已完结」，默认「待办」会把它藏起来
        vm.SelectedStatusFilter = vm.StatusFilters.Single(f => f.Key == "all");
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

    /// <summary>
    /// 组内按优先级排序：同一组里高优先级的任务排在最前面。
    /// </summary>
    [Fact]
    public void Groups_SortTasksByPriority()
    {
        // 全部今天到期 → 都在「今日」组，只差优先级
        var due = Core.Time.LocalTime.ToLocalDateTime(Now.Date.AddHours(18));
        _service.TasksCreate(new CreateTaskInput { Title = "低的", Priority = Priority.Low, DueAt = due });
        _service.TasksCreate(new CreateTaskInput { Title = "高的", Priority = Priority.High, DueAt = due });
        _service.TasksCreate(new CreateTaskInput { Title = "中的", Priority = Priority.Medium, DueAt = due });
        _service.TasksCreate(new CreateTaskInput { Title = "无优先级的", Priority = Priority.None, DueAt = due });

        var vm = CreateVm();

        var today = vm.Groups.Single(g => g.Title == "今日");
        Assert.Equal(["高的", "中的", "低的", "无优先级的"], today.Tasks.Select(t => t.Title).ToArray());
    }

    /// <summary>同优先级时仍按截止时间早的在前。</summary>
    [Fact]
    public void Groups_SamePriority_SortByDueDate()
    {
        // 两个时刻都要在 Now(10:00) 之后，否则早的那个会掉进「逾期」组而不是「今日」组
        _service.TasksCreate(new CreateTaskInput
        {
            Title = "晚的",
            Priority = Priority.High,
            DueAt = Core.Time.LocalTime.ToLocalDateTime(Now.Date.AddHours(20))
        });
        _service.TasksCreate(new CreateTaskInput
        {
            Title = "早的",
            Priority = Priority.High,
            DueAt = Core.Time.LocalTime.ToLocalDateTime(Now.Date.AddHours(12))
        });

        var vm = CreateVm();

        var today = vm.Groups.Single(g => g.Title == "今日");
        Assert.Equal(["早的", "晚的"], today.Tasks.Select(t => t.Title).ToArray());
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

        // 新建表单默认已经带着今天的截止时间，先清掉才是「只挑日期」这个场景
        vm.Form.ClearDueCommand.Execute(null);
        vm.Form.DueDate = new DateTime(2026, 11, 6);

        // 只挑日期时，时间选择器里要直接显示默认的 18:00，而不是留空让人猜
        Assert.Equal(new TimeSpan(18, 0, 0), vm.Form.DueTime);

        Assert.True(vm.Form.Validate());
        vm.SaveFormCommand.Execute(null);

        var task = _service.TasksList().Data!.Single();
        Assert.Equal("2026-11-06T18:00", task.DueAt);
    }

    /// <summary>
    /// 新建任务默认落在「今天」：日期就是今天，时间 18:00（已过 18 点则顺延到 23:59），
    /// 保存出来的 DueAt 也应该是今天。省掉每次新建都要点两下日期。
    /// </summary>
    [Fact]
    public void TaskForm_NewTask_DefaultsToToday()
    {
        var vm = CreateVm();
        vm.NewTaskCommand.Execute(null);

        Assert.Equal(DateTime.Today, vm.Form!.DueDate);
        Assert.NotNull(vm.Form.DueTime);
        Assert.True(vm.Form.DueTime!.Value >= new TimeSpan(18, 0, 0), $"默认时刻不该早于 18:00：{vm.Form.DueTime}");

        vm.Form.Title = "今天的事";
        Assert.True(vm.Form.Validate());
        vm.SaveFormCommand.Execute(null);

        var task = _service.TasksList().Data!.Single();
        Assert.StartsWith(DateTime.Today.ToString("yyyy-MM-dd"), task.DueAt);
    }

    [Fact]
    public void TaskForm_PickTimeWithoutDate_IsRejected()
    {
        var vm = CreateVm();
        vm.NewTaskCommand.Execute(null);
        vm.Form!.Title = "只有时刻";

        // 表单默认带着今天的日期，先清掉才能测「只有时刻」这种非法组合
        vm.Form.ClearDueCommand.Execute(null);
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

    /// <summary>
    /// 选了项目，任务的起止默认继承项目的起止。
    /// 这是「项目的任务起止时间默认为项目的」那条规则的落点。
    /// </summary>
    [Fact]
    public void TaskForm_PickProject_InheritsProjectDateRange()
    {
        var project = _service.ProjectsCreate(new CreateProjectInput
        {
            Name = "带区间的项目",
            StartDate = "2026-12-01",
            DueDate = "2026-12-20"
        }).Data!;

        var vm = CreateVm();
        vm.NewTaskCommand.Execute(null);
        vm.Form!.Title = "继承起止";

        vm.Form.ProjectIndex = Enumerable.Range(0, vm.Form.ProjectChoices.Count)
            .First(i => vm.Form.ProjectChoices[i].Id == project.Id);

        Assert.Equal(new DateTime(2026, 12, 1), vm.Form.StartDate);
        Assert.Equal(new DateTime(2026, 12, 20), vm.Form.DueDate);
        Assert.Equal(TimeSpan.Zero, vm.Form.StartTime);
        Assert.Equal(new TimeSpan(18, 0, 0), vm.Form.DueTime);

        vm.SaveFormCommand.Execute(null);

        var task = _service.TasksList().Data!.Single();
        Assert.Equal("2026-12-01T00:00", task.StartAt);
        Assert.Equal("2026-12-20T18:00", task.DueAt);
    }

    /// <summary>用户自己填过时间之后，换项目不再把ta的时间冲掉。</summary>
    [Fact]
    public void TaskForm_ManualDate_IsNotOverwrittenByProject()
    {
        var project = _service.ProjectsCreate(new CreateProjectInput
        {
            Name = "带区间的项目",
            StartDate = "2026-12-01",
            DueDate = "2026-12-20"
        }).Data!;

        var vm = CreateVm();
        vm.NewTaskCommand.Execute(null);
        vm.Form!.Title = "自己填时间";
        vm.Form.StartDate = new DateTime(2026, 11, 30);
        vm.Form.DueDate = new DateTime(2026, 12, 5);

        vm.Form.ProjectIndex = Enumerable.Range(0, vm.Form.ProjectChoices.Count)
            .First(i => vm.Form.ProjectChoices[i].Id == project.Id);

        Assert.Equal(new DateTime(2026, 11, 30), vm.Form.StartDate);
        Assert.Equal(new DateTime(2026, 12, 5), vm.Form.DueDate);
    }

    /// <summary>截止早于开始是不允许的。</summary>
    [Fact]
    public void TaskForm_DueBeforeStart_IsRejected()
    {
        var vm = CreateVm();
        vm.NewTaskCommand.Execute(null);
        vm.Form!.Title = "顺序错";
        vm.Form.StartDate = new DateTime(2026, 12, 10);
        vm.Form.DueDate = new DateTime(2026, 12, 1);

        Assert.False(vm.Form.Validate());
        Assert.Contains("不能早于", vm.Form.Error);
    }

    /// <summary>任务行上要能看出它从哪天开始 —— 起止继承项目之后，这行信息是唯一线索。</summary>
    [Fact]
    public void Row_MetaText_ShowsStartDate()
    {
        _service.TasksCreate(new CreateTaskInput
        {
            Title = "有开始时间",
            StartAt = "2026-11-05T09:00"
        });

        var vm = CreateVm();

        var row = vm.Groups.SelectMany(g => g.Tasks).Single();
        Assert.True(row.HasStart);
        Assert.Contains("始于 2026-11-05", row.MetaText);
    }

    /// <summary>新任务的默认提醒是「结束前一小时」—— 表单显示与服务层落库必须一致。</summary>
    [Fact]
    public void TaskForm_DefaultsToRemindOneHourBeforeDue()
    {
        var vm = CreateVm();
        vm.NewTaskCommand.Execute(null);

        Assert.Equal(60, vm.Form!.SelectedLead?.Minutes);

        vm.Form.Title = "默认提醒";
        vm.Form.DueDate = new DateTime(2026, 11, 6);
        vm.SaveFormCommand.Execute(null);

        var task = _service.TasksList().Data!.Single();
        Assert.Equal(60, task.RemindBefore);
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

        // 初始只有「待办」是高亮的（进任务页默认先看要做的），否则一排胶囊全是选中态就没法看
        Assert.Single(vm.StatusFilters, f => f.IsSelected);
        Assert.True(vm.StatusFilters.Single(f => f.Key == "todo").IsSelected);

        var doing = vm.StatusFilters.Single(f => f.Key == "doing");
        doing.SelectCommand.Execute(null);

        Assert.Same(doing, vm.SelectedStatusFilter);
        Assert.True(doing.IsSelected);
        Assert.False(vm.StatusFilters.Single(f => f.Key == "todo").IsSelected);
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

    [Fact]
    public void StatusFilter_ToggleButton_KeepsSingleSelection()
    {
        _service.TasksCreate(new CreateTaskInput { Title = "做着的" });
        var vm = CreateVm();

        // ToggleButton 的 IsChecked 双向绑 IsSelected：选中「已完成」应当单选并重新取数
        vm.StatusFilters.Single(f => f.Key == "done").IsSelected = true;

        Assert.Single(vm.StatusFilters, f => f.IsSelected);
        Assert.True(vm.StatusFilters.Single(f => f.Key == "done").IsSelected);
        Assert.Same(vm.StatusFilters.Single(f => f.Key == "done"), vm.SelectedStatusFilter);

        // 试图取消唯一选中的那一项：ToggleButton 语义不允许，应复位回选中
        vm.StatusFilters.Single(f => f.Key == "done").IsSelected = false;
        Assert.True(vm.StatusFilters.Single(f => f.Key == "done").IsSelected);
        Assert.Single(vm.StatusFilters, f => f.IsSelected);
    }
}
