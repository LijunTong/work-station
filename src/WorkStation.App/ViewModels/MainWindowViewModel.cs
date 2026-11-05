using Avalonia;
using Avalonia.Styling;
using Avalonia.Threading;
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

    public BoardViewModel? Board { get; }

    public ProjectsViewModel? Projects { get; }

    public TasksViewModel? Tasks { get; }

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

    public bool IsSettingsActive => Page == AppPage.Settings;

    public bool HasOverdueBadge => OverdueCount > 0;

    public string OverdueBadgeText => OverdueCount > 99 ? "99+" : OverdueCount.ToString();

    partial void OnOverdueCountChanged(int value)
    {
        OnPropertyChanged(nameof(HasOverdueBadge));
        OnPropertyChanged(nameof(OverdueBadgeText));
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
        Settings = new SettingsViewModel(service, ApplyThemeVariant, ShowToast);

        Board.EditTaskRequested = OpenTaskEditor;
        Board.OpenProjectTasksRequested = ShowTasksForProject;
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
        new("设置", "跳转", "设", ["settings", "主题", "theme"], () => Navigate(AppPage.Settings)),
        new("新建任务", "动作", "＋", ["add", "new", "task", "记录"], NewTask),
        new("新建项目", "动作", "＋", ["add", "new", "project"], NewProject),
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
        }

        RefreshBadge();
    }

    public void RefreshBadge()
    {
        if (_service is null) return;

        var result = _service.TasksOverdueCount(LocalTime.ToLocalDateTime(DateTime.Now));
        if (result.Ok)
        {
            OverdueCount = result.Data;
        }
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
}
