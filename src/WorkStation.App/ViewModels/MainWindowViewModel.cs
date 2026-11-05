using Avalonia;
using Avalonia.Styling;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace WorkStation.App.ViewModels;

public enum AppPage
{
    Projects,
    Tasks,
    Settings
}

/// <summary>
/// 外壳视图模型：页面导航、Toast、主题。
///
/// 页面本身是独立的 ViewModel，切换时只做「换 CurrentPage + 触发一次 Reload」，
/// 不销毁重建 —— 这样从任务页跳走再回来，筛选条件还在。
/// </summary>
public partial class MainWindowViewModel : ObservableObject
{
    private readonly DispatcherTimer? _toastTimer;

    public ProjectsViewModel? Projects { get; }

    public TasksViewModel? Tasks { get; }

    public SettingsViewModel? Settings { get; }

    /// <summary>启动失败原因；非空时界面只显示错误页。</summary>
    public string? StartupError { get; }

    public bool HasStartupError => StartupError is not null;

    public bool IsReady => StartupError is null;

    [ObservableProperty]
    private object? _currentPage;

    [ObservableProperty]
    private AppPage _page = AppPage.Projects;

    [ObservableProperty]
    private string _pageTitle = "项目";

    [ObservableProperty]
    private bool _isToastVisible;

    [ObservableProperty]
    private string _toastMessage = string.Empty;

    [ObservableProperty]
    private bool _isToastError;

    [ObservableProperty]
    private string _themeMode = "system";

    // 侧边栏选中态：Classes.active 直接绑这三个布尔值，比在 XAML 里写比较转换器清楚
    public bool IsProjectsActive => Page == AppPage.Projects;

    public bool IsTasksActive => Page == AppPage.Tasks;

    public bool IsSettingsActive => Page == AppPage.Settings;

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
        Projects = new ProjectsViewModel(service, ShowToast);
        Tasks = new TasksViewModel(service, ShowToast);
        Settings = new SettingsViewModel(service, ApplyThemeVariant, ShowToast);

        Projects.NavigateToTasks = ShowTasks;

        ThemeMode = Settings.InitialTheme;
        CurrentPage = Projects;
        PageTitle = "项目";

        _toastTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        _toastTimer.Tick += (_, _) =>
        {
            _toastTimer!.Stop();
            IsToastVisible = false;
        };

        // 主题必须在窗口显示前生效，否则首帧会是系统主题再闪一下
        ApplyThemeVariant(ThemeMode);
        Settings.MarkInitialized();

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

    [RelayCommand]
    private void Navigate(string page)
    {
        if (!Enum.TryParse<AppPage>(page, ignoreCase: true, out var target)) return;
        if (Page == target) return;

        Page = target;
        OnPropertyChanged(nameof(IsProjectsActive));
        OnPropertyChanged(nameof(IsTasksActive));
        OnPropertyChanged(nameof(IsSettingsActive));

        switch (target)
        {
            case AppPage.Projects:
                PageTitle = "项目";
                CurrentPage = Projects;
                Projects?.Reload();
                break;
            case AppPage.Tasks:
                PageTitle = "任务";
                CurrentPage = Tasks;
                Tasks?.Reload();
                break;
            case AppPage.Settings:
                PageTitle = "设置";
                CurrentPage = Settings;
                Settings?.Reload();
                break;
        }
    }

    [RelayCommand]
    private void ShowProjects() => Navigate(nameof(AppPage.Projects));

    [RelayCommand]
    private void ShowTasks() => Navigate(nameof(AppPage.Tasks));

    [RelayCommand]
    private void ShowSettings() => Navigate(nameof(AppPage.Settings));

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
