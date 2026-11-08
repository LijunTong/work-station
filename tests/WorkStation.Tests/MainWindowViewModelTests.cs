using Avalonia.Headless.XUnit;
using Microsoft.Data.Sqlite;
using WorkStation.App.Services;
using WorkStation.App.ViewModels;
using WorkStation.Core.Models;
using WorkStation.Core.Services;

namespace WorkStation.Tests;

/// <summary>外壳：默认页、全局快速添加、徽标、命令面板与页面跳转。</summary>
public class MainWindowViewModelTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly WorkStationService _service;

    // 逾期判定依赖真实时钟，这里直接用「昨天 09:00」保证稳定
    private static string Yesterday =>
        Core.Time.LocalTime.ToLocalDateTime(DateTime.Now.Date.AddDays(-1).AddHours(9));

    public MainWindowViewModelTests()
    {
        _connection = TestDb.OpenMigrated();
        _service = new WorkStationService(_connection, ":memory:");
    }

    public void Dispose() => _connection.Dispose();

    /// <summary>冷启动落在仪表盘：先给总账，再去看板干活。</summary>
    [AvaloniaFact]
    public void StartsOnDashboardPage()
    {
        var vm = new MainWindowViewModel(_service);

        Assert.Equal(AppPage.Dashboard, vm.Page);
        Assert.IsType<DashboardViewModel>(vm.CurrentPage);
        Assert.Equal("仪表盘", vm.PageTitle);
        Assert.True(vm.IsDashboardActive);
        Assert.False(vm.IsBoardActive);
        Assert.False(vm.IsTasksActive);
    }

    /// <summary>项目任务对话框：加载项目与任务，进度卡与步骤清单都就绪。</summary>
    [AvaloniaFact]
    public void ProjectTasksDialog_LoadsProjectAndSteps()
    {
        var project = _service.ProjectsCreate(new CreateProjectInput
        {
            Name = "验收项目",
            Status = ProjectStatus.Active
        }).Data!;
        _service.TasksCreate(new CreateTaskInput { Title = "已做完", ProjectId = project.Id, Status = TaskStatus.Done });
        _service.TasksCreate(new CreateTaskInput { Title = "进行中", ProjectId = project.Id, Status = TaskStatus.Doing });
        _service.TasksCreate(new CreateTaskInput { Title = "独立任务" });

        var dlg = new ProjectTasksDialogViewModel(project.Id, _service, (_, _) => { });

        Assert.True(dlg.HasCard);
        Assert.NotNull(dlg.Card);
        Assert.Equal(project.Id, dlg.ProjectId);
        // 只装入该项目下的任务，独立任务不混进来
        Assert.Equal(2, dlg.Steps.Count);
        // 已完成的排最前（stepper 的竖线才连得起来），未完成的在后
        Assert.Equal("已做完", dlg.Steps[0].Title);
        Assert.True(dlg.Steps[0].IsCompleted);
        Assert.Equal("进行中", dlg.Steps[1].Title);
        Assert.True(dlg.Steps[1].IsActive);
    }

    [AvaloniaFact]
    public void ProjectTasksDialog_QuickAdd_CreatesOneTaskPerLine()
    {
        var project = _service.ProjectsCreate(new CreateProjectInput
        {
            Name = "快速添加",
            Status = ProjectStatus.Active
        }).Data!;

        var dlg = new ProjectTasksDialogViewModel(project.Id, _service, (_, _) => { });
        dlg.QuickAddText = "第一行任务\n第二行任务\n\n第三行任务  ";

        dlg.AddTasksCommand.Execute(null);

        // 每行一个任务，空行忽略；全部建为「待办」并归属本项目
        Assert.Equal(3, dlg.Steps.Count);
        Assert.Contains(dlg.Steps, s => s.Title == "第一行任务");
        Assert.Contains(dlg.Steps, s => s.Title == "第二行任务");
        Assert.Contains(dlg.Steps, s => s.Title == "第三行任务");
        Assert.All(dlg.Steps, s => Assert.True(s.IsPending));

        // 输入框清空
        Assert.Equal(string.Empty, dlg.QuickAddText);
    }

    [AvaloniaFact]
    public void ProjectTasksDialog_MissingProject_SetsError()
    {
        var dlg = new ProjectTasksDialogViewModel("不存在", _service, (_, _) => { });

        Assert.True(dlg.HasError);
        Assert.False(dlg.HasCard);
        Assert.Empty(dlg.Steps);
    }

    /// <summary>项目里快速加的任务，起止默认继承项目的起止区间。</summary>
    [AvaloniaFact]
    public void ProjectTasksDialog_QuickAdd_InheritsProjectDateRange()
    {
        var project = _service.ProjectsCreate(new CreateProjectInput
        {
            Name = "带区间的项目",
            Status = ProjectStatus.Active,
            StartDate = "2026-12-01",
            DueDate = "2026-12-20"
        }).Data!;

        var dlg = new ProjectTasksDialogViewModel(project.Id, _service, (_, _) => { });
        dlg.QuickAddText = "第一条\n第二条";

        dlg.AddTasksCommand.Execute(null);

        var tasks = _service.TasksList().Data!.Where(t => t.ProjectId == project.Id).ToList();
        Assert.Equal(2, tasks.Count);
        Assert.All(tasks, t => Assert.Equal("2026-12-01T00:00", t.StartAt));
        Assert.All(tasks, t => Assert.Equal("2026-12-20T18:00", t.DueAt));
    }

    /// <summary>
    /// 任务清单按「已完成在前」排序：1、2、3 里完成了 1 和 3，
    /// 清单就应该是 1、3、2 —— 已完成的挨在一起，stepper 的竖线才连得起来。
    /// </summary>
    [AvaloniaFact]
    public void ProjectTasksDialog_CompletedTasksComeFirst_InOriginalOrder()
    {
        var project = _service.ProjectsCreate(new CreateProjectInput
        {
            Name = "排序项目",
            Status = ProjectStatus.Active
        }).Data!;

        var first = _service.TasksCreate(new CreateTaskInput { Title = "1", ProjectId = project.Id }).Data!;
        _service.TasksCreate(new CreateTaskInput { Title = "2", ProjectId = project.Id });
        var third = _service.TasksCreate(new CreateTaskInput { Title = "3", ProjectId = project.Id }).Data!;

        _service.TasksSetStatus(first.Id, TaskStatus.Done);
        _service.TasksSetStatus(third.Id, TaskStatus.Done);

        var dlg = new ProjectTasksDialogViewModel(project.Id, _service, (_, _) => { });

        Assert.Equal(["1", "3", "2"], dlg.Steps.Select(s => s.Title).ToArray());
    }

    [AvaloniaFact]
    public void Navigation_UpdatesTitleAndActiveFlags()
    {
        var vm = new MainWindowViewModel(_service);

        vm.ShowTasksCommand.Execute(null);

        Assert.IsType<TasksViewModel>(vm.CurrentPage);
        Assert.Equal("任务", vm.PageTitle);
        Assert.True(vm.IsTasksActive);
        Assert.False(vm.IsBoardActive);

        vm.ShowProjectsCommand.Execute(null);
        Assert.Equal("项目", vm.PageTitle);

        vm.ShowSettingsCommand.Execute(null);
        Assert.Equal("设置", vm.PageTitle);

        vm.ShowBoardCommand.Execute(null);
        Assert.IsType<BoardViewModel>(vm.CurrentPage);
    }

    [AvaloniaFact]
    public void QuickAdd_CreatesTaskAndClearsInput()
    {
        var vm = new MainWindowViewModel(_service);

        // 快速添加是常驻的：在看板上记一条，列表要立刻出现
        vm.ShowBoardCommand.Execute(null);
        vm.QuickAddTitle = "记一件事";

        vm.QuickAddCommand.Execute(null);

        Assert.Equal(string.Empty, vm.QuickAddTitle);
        Assert.Single(_service.TasksList().Data!);
        Assert.Contains("已记下", vm.ToastMessage);

        Assert.Contains(
            vm.Board!.Columns.SelectMany(c => c.Cards),
            card => card.Title == "记一件事");
    }

    /// <summary>
    /// 顶部快速添加也要带今天的截止时间。
    /// 不带的话这条任务会掉进「无期限」，今日/本周/本月任何口径里都看不到 ——
    /// 用户以为「记下了」，其实它不在任何清单里。
    /// </summary>
    [AvaloniaFact]
    public void QuickAdd_SetsDueToday()
    {
        var vm = new MainWindowViewModel(_service);
        vm.QuickAddTitle = "随手记一笔";
        vm.QuickAddCommand.Execute(null);

        var task = _service.TasksList().Data!.Single(t => t.Title == "随手记一笔");
        Assert.StartsWith(DateTime.Today.ToString("yyyy-MM-dd"), task.DueAt);
    }

    [AvaloniaFact]
    public void QuickAdd_BlankInput_DoesNothing()
    {
        var vm = new MainWindowViewModel(_service);
        vm.QuickAddTitle = "   ";

        vm.QuickAddCommand.Execute(null);

        Assert.Empty(_service.TasksList().Data!);
    }

    [AvaloniaFact]
    public void Badge_CountsOverdueAndRefreshesAfterMove()
    {
        var overdue = _service.TasksCreate(new CreateTaskInput
        {
            Title = "昨天的事",
            DueAt = Yesterday
        }).Data!;

        var vm = new MainWindowViewModel(_service);

        Assert.Equal(1, vm.OverdueCount);
        Assert.True(vm.HasOverdueBadge);
        Assert.Equal("1", vm.OverdueBadgeText);

        vm.Board!.MoveTask(overdue.Id, TaskStatus.Done);

        Assert.Equal(0, vm.OverdueCount);
        Assert.False(vm.HasOverdueBadge);
    }

    [AvaloniaFact]
    public void BadgeText_CapsAt99()
    {
        for (var i = 0; i < 120; i++)
        {
            _service.TasksCreate(new CreateTaskInput { Title = $"逾期 {i}", DueAt = Yesterday });
        }

        var vm = new MainWindowViewModel(_service);

        Assert.Equal("99+", vm.OverdueBadgeText);
    }

    [AvaloniaFact]
    public void BoardCardEdit_OpensEditorInPlace_WithoutNavigating()
    {
        var task = _service.TasksCreate(new CreateTaskInput { Title = "从看板点开" }).Data!;
        var vm = new MainWindowViewModel(_service);
        vm.ShowBoardCommand.Execute(null);

        var card = vm.Board!.Columns.SelectMany(c => c.Cards).Single(c => c.Task.Id == task.Id);
        vm.Board.OpenTask(card.Task);

        // 看板编辑任务：不跳转到任务页，直接在看板上弹出编辑弹窗
        Assert.Equal(AppPage.Board, vm.Page);
        Assert.True(vm.Tasks!.IsFormOpen);
        Assert.Equal(task.Id, vm.Tasks.Form!.TaskId);
    }

    [AvaloniaFact]
    public void ProjectBoardLink_OpensTaskDialog()
    {
        var project = _service.ProjectsCreate(new CreateProjectInput { Name = "工作台" }).Data!;
        _service.TasksCreate(new CreateTaskInput { Title = "项目内", ProjectId = project.Id });
        _service.TasksCreate(new CreateTaskInput { Title = "独立" });

        var vm = new MainWindowViewModel(_service);
        vm.ShowBoardCommand.Execute(null);
        var card = vm.Board!.ProjectColumns.SelectMany(c => c.Cards).Single();

        card.OpenTasksCommand.Execute(null);

        // 不再跳转到任务页，而是弹出项目任务浮层
        Assert.Equal(AppPage.Board, vm.Page);
        Assert.NotNull(vm.ProjectTasksDialog);
        Assert.Equal(project.Id, vm.ProjectTasksDialog!.ProjectId);
        Assert.NotNull(vm.ProjectTasksDialog.Card);
    }

    /// <summary>项目页卡片上的「查看任务」应弹出该项目的任务浮层，而不是跳页。</summary>
    [AvaloniaFact]
    public void ProjectsCardOpenTasks_OpensTaskDialog()
    {
        var project = _service.ProjectsCreate(new CreateProjectInput { Name = "工作台" }).Data!;
        _service.TasksCreate(new CreateTaskInput { Title = "项目内", ProjectId = project.Id });

        var vm = new MainWindowViewModel(_service);
        vm.ShowProjectsCommand.Execute(null);
        var card = vm.Projects!.Projects.Single();

        card.OpenTasksCommand.Execute(null);

        Assert.Equal(AppPage.Projects, vm.Page);
        Assert.NotNull(vm.ProjectTasksDialog);
        Assert.Equal(project.Id, vm.ProjectTasksDialog!.ProjectId);
        Assert.NotNull(vm.ProjectTasksDialog.Card);
    }

    /// <summary>
    /// 回归「点项目任务没反应」：ProjectTasksDialog 是普通属性，赋值必须显式触发
    /// PropertyChanged，MainWindow 才会弹窗。属性变了但不通知 = 点了什么都不发生。
    /// </summary>
    [AvaloniaFact]
    public void ProjectTasksDialog_RaisesPropertyChanged_SoWindowCanShowIt()
    {
        var project = _service.ProjectsCreate(new CreateProjectInput { Name = "通知" }).Data!;
        var vm = new MainWindowViewModel(_service);

        string? raised = null;
        vm.PropertyChanged += (_, e) => raised = e.PropertyName;

        vm.ShowProjectsCommand.Execute(null);
        var card = vm.Projects!.Projects.Single();
        card.OpenTasksCommand.Execute(null);

        Assert.Equal(nameof(MainWindowViewModel.ProjectTasksDialog), raised);

        // 关闭也要通知，否则弹窗消失不掉
        raised = null;
        vm.CloseProjectTasksDialog();
        Assert.Equal(nameof(MainWindowViewModel.ProjectTasksDialog), raised);
        Assert.Null(vm.ProjectTasksDialog);
    }

    /// <summary>标题栏一键切换明暗：light↔dark 往返，且同步 IsDarkTheme 给图标按钮。</summary>
    [AvaloniaFact]
    public void ToggleTheme_FlipsLightAndDark()
    {
        var vm = new MainWindowViewModel(_service);
        vm.Settings!.SetThemeCommand.Execute("light");
        Assert.False(vm.IsDarkTheme);

        vm.ToggleThemeCommand.Execute(null);
        Assert.Equal("dark", vm.ThemeMode);
        Assert.True(vm.IsDarkTheme);

        vm.ToggleThemeCommand.Execute(null);
        Assert.Equal("light", vm.ThemeMode);
        Assert.False(vm.IsDarkTheme);
    }

    [AvaloniaFact]
    public void PaletteNewProject_OpensProjectDrawer()
    {
        var vm = new MainWindowViewModel(_service);

        vm.OpenPaletteCommand.Execute(null);
        vm.Palette!.Results.Single(r => r.Title == "新建项目" && r.Hint == "动作")
            .ActivateCommand.Execute(null);

        Assert.Equal(AppPage.Projects, vm.Page);
        Assert.True(vm.Projects!.IsFormOpen, "从面板点「新建项目」应打开项目抽屉");
    }

    [AvaloniaFact]
    public void NewTask_NavigatesAndOpensDrawer()
    {
        var vm = new MainWindowViewModel(_service);

        vm.NewTaskCommand.Execute(null);

        Assert.Equal(AppPage.Tasks, vm.Page);
        Assert.True(vm.Tasks!.IsFormOpen);
    }

    [AvaloniaFact]
    public void PaletteActions_AreWiredToNavigation()
    {
        var vm = new MainWindowViewModel(_service);

        vm.OpenPaletteCommand.Execute(null);
        Assert.True(vm.Palette!.IsOpen);
        Assert.NotEmpty(vm.Palette.Results);

        var settings = vm.Palette.Results.Single(r => r.Title == "设置");
        settings.ActivateCommand.Execute(null);

        Assert.False(vm.Palette.IsOpen);
        Assert.Equal(AppPage.Settings, vm.Page);
    }

    [AvaloniaFact]
    public void ShortcutHelp_Toggles()
    {
        var vm = new MainWindowViewModel(_service);

        vm.ToggleShortcutHelpCommand.Execute(null);
        Assert.True(vm.IsShortcutHelpOpen);

        vm.CloseShortcutHelpCommand.Execute(null);
        Assert.False(vm.IsShortcutHelpOpen);
    }

    // ---------- 提醒 ----------

    [AvaloniaFact]
    public void RemindersPage_NavigatesAndLoads()
    {
        var vm = new MainWindowViewModel(_service);

        vm.ShowRemindersCommand.Execute(null);

        Assert.Equal(AppPage.Reminders, vm.Page);
        Assert.IsType<RemindersViewModel>(vm.CurrentPage);
        Assert.True(vm.IsRemindersActive);
        Assert.False(vm.IsProjectsActive);
        Assert.Equal("提醒", vm.PageTitle);
    }

    [AvaloniaFact]
    public void ReminderBadge_CountsOnlyTriggeredOnes()
    {
        var pending = _service.RemindersCreate(new CreateReminderInput
        {
            Title = "还没到点",
            RemindAt = Core.Time.LocalTime.ToLocalDateTime(DateTime.Now.AddHours(2))
        }).Data!;

        var vm = new MainWindowViewModel(_service);
        Assert.False(vm.HasReminderBadge);

        // 到点弹出但还没处理：徽标出现
        _service.RemindersSetStatus(pending.Id, ReminderStatus.Fired);
        vm.RefreshBadge();

        Assert.Equal(1, vm.ReminderCount);
        Assert.True(vm.HasReminderBadge);
        Assert.Equal("1", vm.ReminderBadgeText);
    }

    [AvaloniaFact]
    public void StartReminderPump_FiresOverdueReminder_AndClickOpensTask()
    {
        // 三类报告提醒默认都开着，会让本用例多出一条通知：先全部关掉，只验任务提醒
        _service.ReportRemindersSet(new ReportReminderSettings
        {
            Daily = new ReportReminderEntry { Enabled = false, Time = "09:00" },
            Weekly = new ReportReminderEntry { Enabled = false, Time = "17:00", DayOfWeek = 5 },
            Monthly = new ReportReminderEntry { Enabled = false, Time = "17:00", DayOfMonth = 31 }
        });

        // 昨天 09:00 截止、提前 1 小时：提醒时刻早就过了
        var task = _service.TasksCreate(new CreateTaskInput
        {
            Title = "补一条周报",
            DueAt = Yesterday,
            RemindBefore = 60
        }).Data!;

        var sink = new RecordingSink();
        var vm = new MainWindowViewModel(_service);
        vm.AttachNotifications(sink);
        vm.StartReminderPump(startTimer: false);
        vm.StopReminderPump();

        Assert.Single(sink.Shown);
        Assert.Equal("任务提醒", sink.Shown[0].Title);
        Assert.Contains("补一条周报", sink.Shown[0].Message);
        Assert.Equal(1, vm.ReminderCount);

        // 点通知：回到任务页并打开这条任务的编辑抽屉
        sink.Shown[0].Click();

        Assert.Equal(AppPage.Tasks, vm.Page);
        Assert.True(vm.Tasks!.IsFormOpen);
        Assert.Equal(task.Id, vm.Tasks.Form!.TaskId);
    }

    [AvaloniaFact]
    public void BoardReminderChip_Removed_ButPaletteStillNavigates()
    {
        var vm = new MainWindowViewModel(_service);

        // 概览条上的「提醒」胶囊已删除（跳提醒页走左侧菜单）
        Assert.DoesNotContain(vm.Board!.OverviewChips, c => c.Key == "reminders");

        // 命令面板里的「提醒」入口仍在
        vm.Palette!.Open();
        vm.Palette.Query = "提醒";
        var item = vm.Palette.Results.First(r => r.Title.Contains("提醒"));
        item.Invoke();

        Assert.Equal(AppPage.Reminders, vm.Page);
    }

    [AvaloniaFact]
    public void Settings_RemindersRoundTrip()
    {
        var vm = new MainWindowViewModel(_service);
        var settings = vm.Settings!;

        Assert.True(settings.ReportDailyEnabled);
        Assert.Equal("09:00", settings.ReportDailyTime);
        Assert.True(settings.CloseToTray);

        settings.ReportDailyEnabled = false;
        settings.ReportDailyTime = "10:30";
        settings.CloseToTray = false;

        var saved = _service.ReportRemindersGet().Data!;
        Assert.False(saved.Daily.Enabled);
        Assert.Equal("10:30", saved.Daily.Time);
        Assert.False(_service.CloseToTrayGet().Data);

        // 重新进入设置页：值从库里读回来
        settings.Reload();
        Assert.False(settings.ReportDailyEnabled);
        Assert.Equal("10:30", settings.ReportDailyTime);
        Assert.False(settings.CloseToTray);
    }

    /// <summary>报告页是独立页面：快捷键 6 / Ctrl+6 都能到达</summary>
    [AvaloniaFact]
    public void NavigateToReports_SwitchesPage()
    {
        var vm = new MainWindowViewModel(_service);

        vm.ShowReportsCommand.Execute(null);

        Assert.Equal(AppPage.Reports, vm.Page);
        Assert.IsType<ReportsViewModel>(vm.CurrentPage);
        Assert.Equal("报告", vm.PageTitle);
        Assert.True(vm.IsReportsActive);
    }

    private sealed class RecordingSink : INotificationSink
    {
        public List<(string Title, string Message, Action Click)> Shown { get; } = [];

        public void Show(string title, string message, Action onClick) =>
            Shown.Add((title, message, onClick));
    }
}
