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

    [AvaloniaFact]
    public void StartsOnBoardPage()
    {
        var vm = new MainWindowViewModel(_service);

        Assert.Equal(AppPage.Board, vm.Page);
        Assert.IsType<BoardViewModel>(vm.CurrentPage);
        Assert.Equal("看板", vm.PageTitle);
        Assert.True(vm.IsBoardActive);
        Assert.False(vm.IsTasksActive);
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
        vm.QuickAddTitle = "记一件事";

        vm.QuickAddCommand.Execute(null);

        Assert.Equal(string.Empty, vm.QuickAddTitle);
        Assert.Single(_service.TasksList().Data!);
        Assert.Contains("已记下", vm.ToastMessage);

        // 看板是当前页，添加后要立刻出现在「无期限」所在的待办列
        Assert.Contains(
            vm.Board!.Columns.SelectMany(c => c.Cards),
            card => card.Title == "记一件事");
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
    public void BoardCardEdit_OpensTaskPageWithForm()
    {
        var task = _service.TasksCreate(new CreateTaskInput { Title = "从看板点开" }).Data!;
        var vm = new MainWindowViewModel(_service);

        var card = vm.Board!.Columns.SelectMany(c => c.Cards).Single(c => c.Task.Id == task.Id);
        vm.Board.OpenTask(card.Task);

        Assert.Equal(AppPage.Tasks, vm.Page);
        Assert.True(vm.Tasks!.IsFormOpen);
        Assert.Equal(task.Id, vm.Tasks.Form!.TaskId);
    }

    [AvaloniaFact]
    public void ProjectBoardLink_FiltersTaskPage()
    {
        var project = _service.ProjectsCreate(new CreateProjectInput { Name = "工作台" }).Data!;
        _service.TasksCreate(new CreateTaskInput { Title = "项目内", ProjectId = project.Id });
        _service.TasksCreate(new CreateTaskInput { Title = "独立" });

        var vm = new MainWindowViewModel(_service);
        var card = vm.Board!.ProjectColumns.SelectMany(c => c.Cards).Single();

        card.OpenTasksCommand.Execute(null);

        Assert.Equal(AppPage.Tasks, vm.Page);
        Assert.Equal(project.Id, vm.Tasks!.SelectedProject!.Id);
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
        // 站会提醒默认开启，会让本用例多出一条通知：先关掉，只验任务提醒
        _service.DailyReviewSet(new DailyReviewSettings { Enabled = false, Time = "09:00" });

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
    public void BoardReminderChip_NavigatesToRemindersPage()
    {
        var vm = new MainWindowViewModel(_service);

        var chip = vm.Board!.OverviewChips.Single(c => c.Key == "reminders");
        chip.SelectCommand.Execute(null);

        Assert.Equal(AppPage.Reminders, vm.Page);
    }

    [AvaloniaFact]
    public void Settings_RemindersRoundTrip()
    {
        var vm = new MainWindowViewModel(_service);
        var settings = vm.Settings!;

        Assert.True(settings.DailyReviewEnabled);
        Assert.Equal("09:00", settings.DailyReviewTime);
        Assert.True(settings.CloseToTray);

        settings.DailyReviewEnabled = false;
        settings.DailyReviewTime = "10:30";
        settings.CloseToTray = false;

        var saved = _service.DailyReviewGet().Data!;
        Assert.False(saved.Enabled);
        Assert.Equal("10:30", saved.Time);
        Assert.False(_service.CloseToTrayGet().Data);

        // 重新进入设置页：值从库里读回来
        settings.Reload();
        Assert.False(settings.DailyReviewEnabled);
        Assert.Equal("10:30", settings.DailyReviewTime);
        Assert.False(settings.CloseToTray);
    }

    private sealed class RecordingSink : INotificationSink
    {
        public List<(string Title, string Message, Action Click)> Shown { get; } = [];

        public void Show(string title, string message, Action onClick) =>
            Shown.Add((title, message, onClick));
    }
}
