using System.Reflection;
using Avalonia;
using Avalonia.Styling;
using Avalonia.Threading;
using WorkStation.App.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
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
    Settings
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

    public CommandPaletteViewModel? Palette { get; }

    /// <summary>启动失败原因；非空时界面只显示错误页。</summary>
    public string? StartupError { get; }

    public bool HasStartupError => StartupError is not null;

    public bool IsReady => StartupError is null;

    [ObservableProperty]
    private object? _currentPage;

    [ObservableProperty]
    private AppPage _page = AppPage.Board;

    [ObservableProperty]
    private string _pageTitle = "看板";

    /// <summary>标题下的补充说明。没有它，标题栏只剩一个词，界面显得没有信息密度。</summary>
    [ObservableProperty]
    private string _pageSubtitle = "手上所有事，按状态摊开";

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

    // 侧边栏选中态：Classes.active 直接绑这四个布尔值，比在 XAML 里写比较转换器清楚
    public bool IsBoardActive => Page == AppPage.Board;

    public bool IsTasksActive => Page == AppPage.Tasks;

    public bool IsProjectsActive => Page == AppPage.Projects;

    public bool IsRemindersActive => Page == AppPage.Reminders;

    public bool IsSettingsActive => Page == AppPage.Settings;

    public bool HasOverdueBadge => OverdueCount > 0;

    public string OverdueBadgeText => OverdueCount > 99 ? "99+" : OverdueCount.ToString();

    public bool HasReminderBadge => ReminderCount > 0;

    public string ReminderBadgeText => ReminderCount > 99 ? "99+" : ReminderCount.ToString();

    /// <summary>
    /// 左下角显示的版本号（形如 <c>V 1.0.1</c>）。取程序集版本，
    /// 不在 XAML 里写死 —— 发版只改 csproj 的 &lt;Version&gt; 一处。
    /// </summary>
    public string VersionText { get; } = FormatVersion();

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

        Board = new BoardViewModel(service, ShowToast, null, RefreshBadge);
        Tasks = new TasksViewModel(service, ShowToast);
        Projects = new ProjectsViewModel(service, ShowToast);
        Reminders = new RemindersViewModel(service, ShowToast, null, RefreshBadge);
        Settings = new SettingsViewModel(service, ApplyThemeVariant, ShowToast);

        Board.EditTaskRequested = OpenTaskEditor;
        Board.OpenProjectTasksRequested = ShowTasksForProject;
        Board.OpenRemindersRequested = ShowReminders;
        Projects.NavigateToTasks = () => ShowTasksForProject(null);

        Palette = new CommandPaletteViewModel(service, BuildPaletteActions())
        {
            OpenTaskRequested = OpenTaskEditor,
            OpenProjectRequested = ShowTasksForProject
        };

        ThemeMode = Settings.InitialTheme;
        Page = AppPage.Board;
        CurrentPage = Board;
        PageTitle = "看板";
        PageSubtitle = "手上所有事，按状态摊开";

        _toastTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        _toastTimer.Tick += (_, _) =>
        {
            _toastTimer!.Stop();
            IsToastVisible = false;
        };

        // 主题必须在窗口显示前生效，否则首帧会是系统主题再闪一下
        ApplyThemeVariant(ThemeMode);
        Settings.MarkInitialized();

        // 首页就是看板，必须在这里取一次数：Navigate 会因为「已经在该页」直接返回
        Board.Reload();
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
        OnPropertyChanged(nameof(IsSettingsActive));

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
            case AppPage.Settings:
                PageTitle = "设置";
                PageSubtitle = "外观偏好与本地数据位置";
                CurrentPage = Settings;
                Settings?.Reload();
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
    private void ShowSettings() => Navigate(AppPage.Settings);

    /// <summary>顶部常驻快速添加：任意页面都能记一条，不打断当前视图。</summary>
    [RelayCommand]
    private void QuickAdd()
    {
        var title = QuickAddTitle.Trim();
        if (title.Length == 0) return;

        var result = _service?.TasksCreate(new CreateTaskInput { Title = title });
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
        _reminderPump.OpenDailyReview = OpenTodayBoard;

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
        _reminderPump?.Tick() ?? new ReminderPumpResult([], false);

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

    private void OpenTodayBoard()
    {
        Navigate(AppPage.Board);
        Board?.ShowToday();
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
        Navigate(AppPage.Tasks);
        Tasks?.OpenEditor(task);
    }

    private void ShowTasksForProject(string? projectId)
    {
        Navigate(AppPage.Tasks);
        if (projectId is not null)
        {
            Tasks?.FocusProject(projectId);
        }
    }

    /// <summary>
    /// 应用主题变体。持久化由 SettingsViewModel 负责 —— 那边是主题状态的唯一真源，
    /// 外壳只做「把值翻译成 Avalonia 的 ThemeVariant」这一件事。
    /// </summary>
    public void ApplyThemeVariant(string mode)
    {
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
    }

    /// <summary>「V 主.次.修订」，去掉 SourceLink 加的 +commit 后缀。</summary>
    private static string FormatVersion()
    {
        var assembly = typeof(MainWindowViewModel).Assembly;

        var informational = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion;

        if (!string.IsNullOrWhiteSpace(informational))
        {
            var plus = informational.IndexOf('+', StringComparison.Ordinal);
            return $"V {(plus > 0 ? informational[..plus] : informational)}";
        }

        var version = assembly.GetName().Version;
        return version is null ? "V 1.0.0" : $"V {version.Major}.{version.Minor}.{version.Build}";
    }
}
