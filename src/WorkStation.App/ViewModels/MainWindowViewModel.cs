using System.Reflection;
using Avalonia;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using WorkStation.App.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SukiUI.Models;
using WorkStation.Core.Board;
using WorkStation.Core.Models;
using WorkStation.Core.Services;
using WorkStation.Core.Time;

namespace WorkStation.App.ViewModels;

public enum AppPage
{
    Board,
    Tasks,
    Projects,
    Reminders,
    Reports,
    Settings,
    Dashboard
}

/// <summary>
/// 外壳视图模型：页面导航、全局快速添加、Toast、主题、命令面板。
///
/// 页面本身是独立的 ViewModel，切换时只做「换 CurrentPage + 触发一次 Reload」，
/// 不销毁重建 —— 这样从任务页跳走再回来，筛选条件还在。
/// </summary>
public partial class MainWindowViewModel : ObservableObject
{
    private readonly WorkStationService? _service;
    private readonly DispatcherTimer? _toastTimer;

    private ReminderPump? _reminderPump;
    private DispatcherTimer? _pumpTimer;
    private INotificationSink? _notificationSink;

    public BoardViewModel? Board { get; }

    public ProjectsViewModel? Projects { get; }

    public TasksViewModel? Tasks { get; }

    public RemindersViewModel? Reminders { get; }

    public SettingsViewModel? Settings { get; }

    /// <summary>仪表盘：只读统计页，与看板的分工见 <see cref="DashboardViewModel"/>。</summary>
    public DashboardViewModel? Dashboard { get; }

    /// <summary>报告页：日报 / 周报 / 月报。</summary>
    public ReportsViewModel? Reports { get; }

    /// <summary>从项目卡片「查看任务」点进来时挂上的弹窗内容；为 null 表示没有弹窗。</summary>
    public ProjectTasksDialogViewModel? ProjectTasksDialog { get; private set; }

    public CommandPaletteViewModel? Palette { get; }

    /// <summary>启动失败原因；非空时界面只显示错误页。</summary>
    public string? StartupError { get; }

    public bool HasStartupError => StartupError is not null;

    public bool IsReady => StartupError is null;

    [ObservableProperty]
    private object? _currentPage;

    [ObservableProperty]
    private AppPage _page = AppPage.Dashboard;

    [ObservableProperty]
    private string _pageTitle = "仪表盘";

    /// <summary>标题下的补充说明。没有它，标题栏只剩一个词，界面显得没有信息密度。</summary>
    [ObservableProperty]
    private string _pageSubtitle = "今日、本周、本月，一眼看清";

    [ObservableProperty]
    private string _quickAddTitle = string.Empty;

    [ObservableProperty]
    private int _overdueCount;

    /// <summary>已弹出、还没处理的提醒数。驱动侧边栏「提醒」徽标与概览条。</summary>
    [ObservableProperty]
    private int _reminderCount;

    [ObservableProperty]
    private bool _isToastVisible;

    [ObservableProperty]
    private string _toastMessage = string.Empty;

    [ObservableProperty]
    private bool _isToastError;

    [ObservableProperty]
    private bool _isShortcutHelpOpen;

    [ObservableProperty]
    private string _themeMode = "system";

    /// <summary>
    /// 侧边栏是否展开。折叠状态跟着用户走，存进设置，下次启动按原样恢复。
    /// </summary>
    [ObservableProperty]
    private bool _isSidebarExpanded = true;

    /// <summary>当前是否深色主题。标题栏快速切换按钮据此显示太阳/月亮。</summary>
    public bool IsDarkTheme => ThemeMode == "dark";

    partial void OnThemeModeChanged(string value) => OnPropertyChanged(nameof(IsDarkTheme));

    partial void OnIsSidebarExpandedChanged(bool value) =>
        _service?.SettingsSet("sidebarExpanded", value);

    /// <summary>标题栏一键折叠/展开侧边栏，快捷键 Ctrl+B 也走这里。</summary>
    [RelayCommand]
    private void ToggleSidebar() => IsSidebarExpanded = !IsSidebarExpanded;

    /// <summary>标题栏一键切换明暗：现在是深色就切浅色，否则切深色。持久化走 Settings 的唯一真源。</summary>
    [RelayCommand]
    private void ToggleTheme()
    {
        var next = ThemeMode == "dark" ? "light" : "dark";
        if (Settings is { } settings) settings.SetThemeCommand.Execute(next);
    }

    // 侧边栏选中态：Classes.active 直接绑这四个布尔值，比在 XAML 里写比较转换器清楚
    public bool IsBoardActive => Page == AppPage.Board;

    public bool IsTasksActive => Page == AppPage.Tasks;

    public bool IsProjectsActive => Page == AppPage.Projects;

    public bool IsRemindersActive => Page == AppPage.Reminders;

    public bool IsReportsActive => Page == AppPage.Reports;

    public bool IsSettingsActive => Page == AppPage.Settings;

    public bool IsDashboardActive => Page == AppPage.Dashboard;

    public bool HasOverdueBadge => OverdueCount > 0;

    public string OverdueBadgeText => OverdueCount > 99 ? "99+" : OverdueCount.ToString();

    public bool HasReminderBadge => ReminderCount > 0;

    public string ReminderBadgeText => ReminderCount > 99 ? "99+" : ReminderCount.ToString();

    /// <summary>
    /// 关闭窗口时是否驻留托盘。窗口的 Closing 里读取：
    /// 设成 false 或托盘不可用，关闭就是退出。
    /// </summary>
    public bool CloseToTray =>
        _service?.CloseToTrayGet() is { Ok: true, Data: var value } ? value : true;

    partial void OnOverdueCountChanged(int value)
    {
        OnPropertyChanged(nameof(HasOverdueBadge));
        OnPropertyChanged(nameof(OverdueBadgeText));
    }

    partial void OnReminderCountChanged(int value)
    {
        OnPropertyChanged(nameof(HasReminderBadge));
        OnPropertyChanged(nameof(ReminderBadgeText));
    }

    /// <summary>设计器与 XAML 预览用。</summary>
    public MainWindowViewModel()
    {
    }

    public MainWindowViewModel(AppServices services)
        : this(services.Service, services.RecoveredFrom, services.BackupCreated)
    {
    }

    /// <summary>不依赖 AppServices 的构造：测试与无头环境直接注入服务层。</summary>
    public MainWindowViewModel(
        Core.Services.WorkStationService service,
        string? recoveredFrom = null,
        bool backupCreated = false)
    {
        _service = service;

        // 折叠状态恢复：直接写字段，避免初始化时又往设置里回写一次。
        // 用 bool? 读取，缺省键返回 null 才能和「用户明确存了 false」区分开。
        var savedSidebar = service.SettingsGet<bool?>("sidebarExpanded");
        if (savedSidebar is { Ok: true, Data: { } expanded }) _isSidebarExpanded = expanded;

        Board = new BoardViewModel(service, ShowToast, null, RefreshBadge);
        Tasks = new TasksViewModel(service, ShowToast);
        Projects = new ProjectsViewModel(service, ShowToast);
        Reminders = new RemindersViewModel(service, ShowToast, null, RefreshBadge);
        Settings = new SettingsViewModel(service, ApplyThemeVariant, ApplyThemeColor, ShowToast, ApplyCustomThemeColor, ApplyFont);
        Dashboard = new DashboardViewModel(service, ShowToast);
        Reports = new ReportsViewModel(service, ShowToast);

        Board.EditTaskRequested = OpenTaskEditorInPlace;
        Board.OpenProjectTasksRequested = ShowTasksForProject;
        Projects.NavigateToTasks = ShowTasksForProject;

        // 仪表盘上的数字点一下 → 跳去看板并带上对应口径（看板是干活的地方，仪表盘只报数）
        Dashboard.JumpToBoardRequested = ShowBoardWithScope;

        // 仪表盘上的实体数据同样可以点开：项目弹任务清单，任务开编辑弹窗
        Dashboard.OpenProjectRequested = ShowTasksForProject;
        Dashboard.OpenTaskRequested = OpenTaskEditorInPlace;

        Palette = new CommandPaletteViewModel(service, BuildPaletteActions())
        {
            OpenTaskRequested = OpenTaskEditor,
            OpenProjectRequested = ShowTasksForProject
        };

        ThemeMode = Settings.InitialTheme;

        // 启动落在仪表盘：先给一张「今天/本周/本月」的总账，比直接扎进看板更适合冷启动。
        Page = AppPage.Dashboard;
        CurrentPage = Dashboard;
        PageTitle = "仪表盘";
        PageSubtitle = "今日、本周、本月，一眼看清";

        _toastTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        _toastTimer.Tick += (_, _) =>
        {
            _toastTimer!.Stop();
            IsToastVisible = false;
        };

        // 主题必须在窗口显示前生效，否则首帧会是系统主题再闪一下
        ApplyThemeVariant(ThemeMode);
        Settings.ApplySavedColor();
        ApplyFont(Settings.SelectedFont);
        Settings.MarkInitialized();

        // 首页（仪表盘）必须在这里取一次数：Navigate 会因为「已经在该页」直接返回
        Dashboard.Reload();
        RefreshBadge();

        if (recoveredFrom is { } recovered)
        {
            ShowToast($"数据库已损坏，已重建空库；原文件保留在 {recovered}", isError: true);
        }
        else if (backupCreated)
        {
            ShowToast("已创建今日数据备份");
        }
    }

    private MainWindowViewModel(string startupError)
    {
        StartupError = startupError;
        CurrentPage = null;
    }

    public static MainWindowViewModel StartupFailed(Exception error) =>
        new($"启动失败：{error.Message}");

    private IReadOnlyList<PaletteAction> BuildPaletteActions() =>
    [
        new("任务看板", "跳转", "看", ["board", "kanban", "首页"], () =>
        {
            Navigate(AppPage.Board);
            Board?.SwitchBoardCommand.Execute(nameof(BoardMode.Tasks));
        }),
        new("项目看板", "跳转", "看", ["board", "project", "kanban"], () =>
        {
            Navigate(AppPage.Board);
            Board?.SwitchBoardCommand.Execute(nameof(BoardMode.Projects));
        }),
        new("全部任务", "跳转", "任", ["tasks", "list", "清单"], () => Navigate(AppPage.Tasks)),
        new("全部项目", "跳转", "项", ["projects", "list"], () => Navigate(AppPage.Projects)),
        new("提醒", "跳转", "铃", ["reminder", "提醒", "alarm"], () => Navigate(AppPage.Reminders)),
        new("报告", "跳转", "报", ["report", "报告", "日报", "周报", "月报"], () => Navigate(AppPage.Reports)),
        new("仪表盘", "跳转", "表", ["dashboard", "仪表盘", "统计", "概览"], () => Navigate(AppPage.Dashboard)),
        new("设置", "跳转", "设", ["settings", "主题", "theme"], () => Navigate(AppPage.Settings)),
        new("新建任务", "动作", "＋", ["add", "new", "task", "记录"], NewTask),
        new("新建项目", "动作", "＋", ["add", "new", "project"], NewProject),
        new("新建提醒", "动作", "＋", ["add", "reminder", "提醒"], NewReminder),
        new("显示快捷键", "帮助", "？", ["help", "keyboard", "快捷键"], () => IsShortcutHelpOpen = true)
    ];

    [RelayCommand]
    private void Navigate(string page)
    {
        if (!Enum.TryParse<AppPage>(page, ignoreCase: true, out var target)) return;
        Navigate(target);
    }

    private void Navigate(AppPage target)
    {
        if (Page == target) return;

        Page = target;
        OnPropertyChanged(nameof(IsBoardActive));
        OnPropertyChanged(nameof(IsTasksActive));
        OnPropertyChanged(nameof(IsProjectsActive));
        OnPropertyChanged(nameof(IsRemindersActive));
        OnPropertyChanged(nameof(IsReportsActive));
        OnPropertyChanged(nameof(IsSettingsActive));
        OnPropertyChanged(nameof(IsDashboardActive));

        switch (target)
        {
            case AppPage.Board:
                PageTitle = "看板";
                PageSubtitle = "手上所有事，按状态摊开";
                CurrentPage = Board;
                Board?.Reload();
                break;
            case AppPage.Tasks:
                PageTitle = "任务";
                PageSubtitle = "今天要做的事，和工作台里攒下的事";
                CurrentPage = Tasks;
                Tasks?.Reload();
                break;
            case AppPage.Projects:
                PageTitle = "项目";
                PageSubtitle = "管理项目与整体进度";
                CurrentPage = Projects;
                Projects?.Reload();
                break;
            case AppPage.Reminders:
                PageTitle = "提醒";
                PageSubtitle = "到点会弹出来的事，和已经弹过的";
                CurrentPage = Reminders;
                Reminders?.Reload();
                break;
            case AppPage.Reports:
                PageTitle = "报告";
                PageSubtitle = "日报、周报、月报，写点总结的地方";
                CurrentPage = Reports;
                Reports?.Reload();
                break;
            case AppPage.Settings:
                PageTitle = "设置";
                PageSubtitle = "外观偏好与本地数据位置";
                CurrentPage = Settings;
                Settings?.Reload();
                break;
            case AppPage.Dashboard:
                PageTitle = "仪表盘";
                PageSubtitle = "今日、本周、本月，一眼看清";
                CurrentPage = Dashboard;
                Dashboard?.Reload();
                break;
        }

        RefreshBadge();
    }

    [RelayCommand]
    private void ShowBoard() => Navigate(AppPage.Board);

    [RelayCommand]
    private void ShowProjects() => Navigate(AppPage.Projects);

    [RelayCommand]
    private void ShowTasks() => Navigate(AppPage.Tasks);

    [RelayCommand]
    private void ShowReminders() => Navigate(AppPage.Reminders);

    [RelayCommand]
    private void ShowReports() => Navigate(AppPage.Reports);

    [RelayCommand]
    private void ShowSettings() => Navigate(AppPage.Settings);

    [RelayCommand]
    private void ShowDashboard() => Navigate(AppPage.Dashboard);

    /// <summary>
    /// 仪表盘跳看板：切到看板页并套用对应的时间口径。
    /// 口径由看板自己维护，仪表盘只负责把「用户想看哪一块」传过去。
    /// </summary>
    private void ShowBoardWithScope(DashboardJumpTarget target)
    {
        Navigate(AppPage.Board);

        switch (target)
        {
            case DashboardJumpTarget.Today:
                Board?.ShowScope(BoardTimeScope.Today);
                break;
            case DashboardJumpTarget.Overdue:
                Board?.ShowScope(BoardTimeScope.Overdue);
                break;
            case DashboardJumpTarget.Week:
                Board?.ShowScope(BoardTimeScope.Week);
                break;
            case DashboardJumpTarget.Blocked:
                Board?.ShowBlockedOnly();
                break;
            default:
                Board?.ShowScope(BoardTimeScope.All);
                break;
        }
    }

    /// <summary>顶部常驻快速添加：任意页面都能记一条，不打断当前视图。</summary>
    [RelayCommand]
    private void QuickAdd()
    {
        var title = QuickAddTitle.Trim();
        if (title.Length == 0) return;

        // 快速添加也要带默认截止：跟「新建任务」表单同一套（今天 18:00，过点顺延 23:59）。
        // 不带时间的话，这些任务会掉进「无期限」，在今日/本周/本月任何口径里都看不见 ——
        // 用户以为「记下了」，其实它不在任何清单里。
        var result = _service?.TasksCreate(new CreateTaskInput
        {
            Title = title,
            DueAt = TaskDefaults.DueAtFor(DateTime.Now)
        });
        if (result is { Ok: false })
        {
            ShowToast(result.Error?.Message ?? "添加失败", true);
            return;
        }

        QuickAddTitle = string.Empty;
        ShowToast("已记下", false);
        RefreshActivePage();
    }

    [RelayCommand]
    private void NewTask()
    {
        Navigate(AppPage.Tasks);
        Tasks?.NewTaskCommand.Execute(null);
    }

    [RelayCommand]
    private void NewProject()
    {
        Navigate(AppPage.Projects);
        Projects?.NewProjectCommand.Execute(null);
    }

    [RelayCommand]
    private void NewReminder()
    {
        Navigate(AppPage.Reminders);
        Reminders?.NewReminderCommand.Execute(null);
    }

    [RelayCommand]
    private void OpenPalette() => Palette?.Open();

    [RelayCommand]
    private void ClosePalette() => Palette?.Close();

    [RelayCommand]
    private void ToggleShortcutHelp() => IsShortcutHelpOpen = !IsShortcutHelpOpen;

    [RelayCommand]
    private void CloseShortcutHelp() => IsShortcutHelpOpen = false;

    [RelayCommand]
    private void HideToast() => IsToastVisible = false;

    /// <summary>统一提示入口。isError 只影响配色，不影响停留时长。</summary>
    public void ShowToast(string message, bool isError = false)
    {
        ToastMessage = message;
        IsToastError = isError;
        IsToastVisible = true;

        if (_toastTimer is null) return;
        _toastTimer.Stop();
        _toastTimer.Start();
    }

    /// <summary>当前页重新取数。快速添加、看板拖拽之后调用。</summary>
    public void RefreshActivePage()
    {
        switch (Page)
        {
            case AppPage.Board:
                Board?.Reload();
                break;
            case AppPage.Tasks:
                Tasks?.Reload();
                break;
            case AppPage.Projects:
                Projects?.Reload();
                break;
            case AppPage.Reminders:
                Reminders?.Reload();
                break;
            case AppPage.Reports:
                Reports?.Reload();
                break;
            case AppPage.Dashboard:
                Dashboard?.Reload();
                break;
        }

        RefreshBadge();
    }

    public void RefreshBadge()
    {
        if (_service is null) return;

        var now = LocalTime.ToLocalDateTime(DateTime.Now);

        var result = _service.TasksOverdueCount(now);
        if (result.Ok)
        {
            OverdueCount = result.Data;
        }

        var reminders = _service.RemindersCounts();
        if (reminders.Ok && reminders.Data is { } counts)
        {
            ReminderCount = counts.Unhandled;
        }
    }

    // ---------- 提醒调度 ----------

    /// <summary>窗口就绪后把「通知怎么弹」注入进来。无头测试换成记录器即可。</summary>
    public void AttachNotifications(INotificationSink sink)
    {
        _notificationSink = sink;
        if (_reminderPump is not null) _reminderPump.Sink = sink;
    }

    /// <param name="startTimer">
    /// 测试传 false：只装配调度器与通知出口，不启动真实定时器。
    /// </param>
    public void StartReminderPump(bool startTimer = true)
    {
        if (_service is null || _reminderPump is not null) return;

        _reminderPump = new ReminderPump(_service) { Sink = _notificationSink };
        _reminderPump.OpenReminder = OpenReminderFromNotification;
        _reminderPump.OpenReport = OpenReportFromNotification;

        // 启动先修一次漂移：应用没开的时候改过的截止时间，在这里补上或清掉
        _service.RemindersSync();

        if (startTimer)
        {
            // 20 秒：分钟级的提醒不需要更密，也不至于晚一分钟才弹
            _pumpTimer = new DispatcherTimer(TimeSpan.FromSeconds(20), DispatcherPriority.Background,
                (_, _) => PumpOnce());
            _pumpTimer.Start();
        }

        // 立刻跑一次：关机期间到点的那些，开机后马上补出来
        PumpOnce();
    }

    public void StopReminderPump()
    {
        _pumpTimer?.Stop();
        _pumpTimer = null;
    }

    /// <summary>跑一次调度节拍。测试直接调用，不必等定时器。</summary>
    public ReminderPumpResult PumpReminders() =>
        _reminderPump?.Tick() ?? new ReminderPumpResult([], []);

    private void PumpOnce()
    {
        var result = PumpReminders();
        if (!result.Any) return;

        RefreshBadge();
        if (Page == AppPage.Reminders) Reminders?.Reload();
    }

    private void OpenReminderFromNotification(ReminderItem item)
    {
        if (item.Kind == ReminderKind.TaskDue && item.TaskId is { } taskId && OpenTaskById(taskId)) return;
        ShowRemindersCommand.Execute(null);
    }

    /// <summary>报告提醒点了通知：进报告页并切到对应的报告类型。</summary>
    private void OpenReportFromNotification(ReportKind kind)
    {
        Navigate(AppPage.Reports);
        Reports?.SelectKindCommand.Execute(kind);
    }

    /// <summary>按 Id 打开任务编辑抽屉。通知点击时只知道 task_id。</summary>
    public bool OpenTaskById(string taskId)
    {
        if (_service is null) return false;

        var result = _service.TasksGet(taskId);
        if (!result.Ok || result.Data is not { } task)
        {
            ShowToast(result.Error?.Message ?? "任务已不存在", true);
            return false;
        }

        OpenTaskEditor(task);
        return true;
    }

    private void OpenTaskEditor(TaskItem task)
    {
        // 命令面板/提醒里「打开任务」：仍跳到任务页并打开编辑抽屉（提醒的既定行为是「回到任务页」）。
        Navigate(AppPage.Tasks);
        Tasks?.OpenEditor(task);
    }

    private void OpenTaskEditorInPlace(TaskItem task)
    {
        // 看板里「编辑任务」：不跳转到任务页，直接在当前页（看板）弹出编辑弹窗，改完留在原地。
        // 保存后由弹窗关闭钩子刷新当前页，让改动立刻反映在看板上（见 MainWindow.ShowEditorDialog）。
        Tasks?.OpenEditor(task);
    }

    private void ShowTasksForProject(string? projectId)
    {
        // 从项目进任务列表：不跳页，直接弹窗展示（圆形进度 + 垂直 stepper）。
        // projectId 为空（理论上不会）时退回原行为：去任务页。
        if (projectId is null)
        {
            Navigate(AppPage.Tasks);
            return;
        }

        ProjectTasksDialog = new ProjectTasksDialogViewModel(projectId, _service!, ShowToast);
        // 普通属性不会自动通知：必须显式触发，MainWindow 靠这个通知弹出弹窗
        OnPropertyChanged(nameof(ProjectTasksDialog));
    }

    public void CloseProjectTasksDialog()
    {
        ProjectTasksDialog = null;
        OnPropertyChanged(nameof(ProjectTasksDialog));
    }

    /// <summary>
    /// 应用主题变体。持久化由 SettingsViewModel 负责 —— 那边是主题状态的唯一真源，
    /// 外壳只做「把值翻译成 Avalonia 的 ThemeVariant」这一件事。
    /// </summary>
    public void ApplyThemeVariant(string mode)
    {
        var previous = ThemeMode;
        ThemeMode = mode switch
        {
            "light" => "light",
            "dark" => "dark",
            _ => "system"
        };

        var application = Application.Current;
        if (application is null) return;

        application.RequestedThemeVariant = ThemeMode switch
        {
            "light" => ThemeVariant.Light,
            "dark" => ThemeVariant.Dark,
            _ => ThemeVariant.Default
        };

        // 统计数字、优先级色条、状态点的画刷由 VM 按「当时的主题」从 Suki 资源取，
        // 是普通 CLR 属性，换主题不会自动重新求值。重建当前页视图让绑定重新取值。
        if (previous == ThemeMode || !Dispatcher.UIThread.CheckAccess()) return;
        Dispatcher.UIThread.Post(() =>
        {
            var page = CurrentPage;
            if (page is null) return;
            CurrentPage = null;
            CurrentPage = page;
        }, DispatcherPriority.Background);
    }

    /// <summary>
    /// 应用主题色（SukiUI 的主色）。主色资源键的注册依赖这个值，切到新颜色时 Suki 会按当前明暗
    /// 重算主色阶 120/150。窗口里的 ActualThemeVariantChanged 也会重新套用当前生效的主题色，
    /// 所以明暗切换后主题色不会丢。
    /// </summary>
    public void ApplyThemeColor(SukiUI.Enums.SukiColor color)
    {
        var application = Application.Current;
        if (application is null) return;

        var theme = SukiUI.SukiTheme.GetInstance(application);

        // 必须走 SukiColorTheme 重载，不能走枚举重载。
        // 枚举重载是「给 ThemeColor 属性赋值，靠属性变化触发换色」，
        // 而自定义主题并不改 ThemeColor —— 自定义色生效后 ThemeColor 还停在原来那个内置值上，
        // 这时再点同一个内置色，属性值没变、不会触发换色，用户点了像没反应。
        if (SukiUI.SukiTheme.DefaultColorThemes.TryGetValue(color, out var builtIn))
        {
            theme.ChangeColorTheme(builtIn);
            return;
        }

        theme.ChangeColorTheme(color);
    }

    /// <summary>
    /// 应用自定义主题色：主色 + 强调色自己造一个 SukiColorTheme 套上去。
    ///
    /// 不用 AddColorTheme：那个方法往 Suki 的主题列表里塞一条，重复添加同一份会抛
    /// InvalidOperationException（record 相等性比的是全部字段）。设置页只有一个自定义主题，
    /// 不需要进那个列表，直接 ChangeColorTheme 就够了。
    /// </summary>
    public void ApplyCustomThemeColor(Color primary, Color accent)
    {
        var application = Application.Current;
        if (application is null) return;

        var theme = SukiUI.SukiTheme.GetInstance(application);
        theme.ChangeColorTheme(new SukiColorTheme("自定义", primary, accent));
    }

    /// <summary>
    /// 应用界面字体。
    ///
    /// SukiUI 给 TextBlock / Button / TextBox 等单独 Setter 了 DefaultFontFamily，
    /// 而 ComboBox 的下拉项、ToolTip 这些浮层不在那份选择器里，只继承窗口的 FontFamily ——
    /// 所以两处都要换，界面才会整块统一。
    /// 选了「默认」时把 DefaultFontFamily 删掉，退回 SukiUI 自带的那套（Quicksand + 微软雅黑回退）。
    /// </summary>
    public void ApplyFont(string? familyName)
    {
        var application = Application.Current;
        if (application is null) return;

        if (string.IsNullOrWhiteSpace(familyName) || familyName == SettingsViewModel.DefaultFontName)
        {
            application.Resources.Remove("DefaultFontFamily");
            application.Resources["UiFontFamily"] = DefaultWindowFont;
            return;
        }

        // 中文回退链必须留着：用户挑的多半是西文字体，没有回退的话汉字会掉到默认字体上。
        var font = new FontFamily($"{familyName}, Microsoft YaHei UI, Microsoft YaHei, Segoe UI");
        application.Resources["DefaultFontFamily"] = font;
        application.Resources["UiFontFamily"] = font;
    }

    /// <summary>窗口级字体的默认值，与 App.axaml 里最初写死的那串保持一致。</summary>
    private static FontFamily DefaultWindowFont { get; } =
        new("Microsoft YaHei UI, Microsoft YaHei, Segoe UI");

}

/// <summary>
/// 删除确认的统一契约：ViewModel 只维护「有没有待删对象」和两个命令，
/// 弹窗本体由 MainWindow 用 SukiDialogBuilder 呈现，页面里不再自绘遮罩。
/// </summary>
public interface IDeleteConfirmation
{
    bool IsDeleteConfirmOpen { get; }
    string DeleteTargetName { get; }
    System.Windows.Input.ICommand Cancel { get; }
    System.Windows.Input.ICommand Confirm { get; }
}

/// <summary>
/// 编辑弹框的统一契约：页面 VM 只维护「有没有表单」和表单对象，
/// 弹框本体一律由 MainWindow 用 SukiDialogBuilder 呈现。
/// </summary>
public interface IEditorDialog : System.ComponentModel.INotifyPropertyChanged
{
    bool IsFormOpen { get; }
    object? Form { get; }
    string DialogTemplateKey { get; }
    void DismissForm();
}

/// <summary>表单 VM 暴露给外壳的两个动作，用来接 Ctrl+Enter / Esc。</summary>
public interface IEditorForm
{
    System.Windows.Input.ICommand Save { get; }
    System.Windows.Input.ICommand Cancel { get; }
}
