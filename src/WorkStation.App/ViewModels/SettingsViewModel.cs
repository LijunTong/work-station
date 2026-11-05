using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WorkStation.Core;
using WorkStation.Core.Models;
using WorkStation.Core.Reminders;
using WorkStation.Core.Services;

namespace WorkStation.App.ViewModels;

/// <summary>
/// 设置页。主题状态只在这里保存一份，外壳只负责把它应用到 Application.RequestedThemeVariant，
/// 避免两个 VM 各存一份而不同步。
/// </summary>
public partial class SettingsViewModel : ObservableObject
{
    private readonly WorkStationService _service;
    private readonly Action<string> _applyTheme;
    private readonly Action<string, bool> _toast;
    private bool _initialized;

    public SettingsViewModel(
        WorkStationService service,
        Action<string> applyTheme,
        Action<string, bool> toast)
    {
        _service = service;
        _applyTheme = applyTheme;
        _toast = toast;

        var saved = service.SettingsGet<string>("theme");
        _themeMode = Normalize(saved.Data);

        var review = service.DailyReviewGet();
        _dailyReviewEnabled = review is { Ok: true, Data: { } config } && config.Enabled;
        _dailyReviewTime = review is { Ok: true, Data: { } conf } ? conf.Time : DailyReviewPlanner.DefaultTime;

        var closeToTray = service.CloseToTrayGet();
        _closeToTray = closeToTray is not { Ok: true } || closeToTray.Data;

        BuildTimeOptions(_dailyReviewTime);
    }

    [ObservableProperty]
    private string _themeMode = "system";

    public bool IsSystemTheme => ThemeMode == "system";

    public bool IsLightTheme => ThemeMode == "light";

    public bool IsDarkTheme => ThemeMode == "dark";

    public string DatabasePath => _service.Info().DatabasePath;

    public string UserDataPath => _service.Info().UserDataPath;

    public string Version => _service.Info().Version;

    public string BackupDirectory => Path.Combine(UserDataPath, "backups");

    // ---------- 提醒 ----------

    [ObservableProperty]
    private bool _dailyReviewEnabled;

    [ObservableProperty]
    private string _dailyReviewTime = DailyReviewPlanner.DefaultTime;

    [ObservableProperty]
    private bool _closeToTray;

    /// <summary>整点与半点：站会时间几乎都落在这些点上，避免手输 9:00 / 09:0 这类格式差异。</summary>
    public ObservableCollection<string> DailyReviewTimeOptions { get; } = [];

    private void BuildTimeOptions(string current)
    {
        DailyReviewTimeOptions.Clear();
        for (var hour = 6; hour <= 22; hour++)
        {
            DailyReviewTimeOptions.Add($"{hour:00}:00");
            DailyReviewTimeOptions.Add($"{hour:00}:30");
        }

        // 历史值不在预设里（比如 09:15）：补进去，否则下拉会显示空白
        if (!DailyReviewTimeOptions.Contains(current)) DailyReviewTimeOptions.Add(current);
    }

    partial void OnDailyReviewEnabledChanged(bool value)
    {
        if (!_initialized) return;
        SaveDailyReview();
    }

    partial void OnDailyReviewTimeChanged(string value)
    {
        if (!_initialized) return;
        SaveDailyReview();
    }

    partial void OnCloseToTrayChanged(bool value)
    {
        if (!_initialized) return;

        var result = _service.CloseToTraySet(value);
        if (!result.Ok) _toast(result.Error?.Message ?? "窗口行为保存失败", true);
    }

    private void SaveDailyReview()
    {
        var result = _service.DailyReviewSet(new DailyReviewSettings
        {
            Enabled = DailyReviewEnabled,
            Time = DailyReviewTime
        });

        if (!result.Ok)
        {
            _toast(result.Error?.Message ?? "站会提醒保存失败", true);
        }
    }

    partial void OnThemeModeChanged(string value)
    {
        OnPropertyChanged(nameof(IsSystemTheme));
        OnPropertyChanged(nameof(IsLightTheme));
        OnPropertyChanged(nameof(IsDarkTheme));

        if (!_initialized) return;

        var result = _service.SettingsSet("theme", value);
        if (!result.Ok)
        {
            _toast(result.Error?.Message ?? "主题保存失败", true);
            return;
        }

        _applyTheme(value);
    }

    public void Reload()
    {
        var saved = _service.SettingsGet<string>("theme");
        ThemeMode = Normalize(saved.Data);

        var review = _service.DailyReviewGet();
        if (review is { Ok: true, Data: { } config })
        {
            DailyReviewEnabled = config.Enabled;
            if (!DailyReviewTimeOptions.Contains(config.Time)) BuildTimeOptions(config.Time);
            DailyReviewTime = config.Time;
        }

        var closeToTray = _service.CloseToTrayGet();
        if (closeToTray is { Ok: true }) CloseToTray = closeToTray.Data;

        _initialized = true;
    }

    /// <summary>启动阶段标记为已初始化，使后续切换能落盘；初始值本身不回写。</summary>
    public void MarkInitialized() => _initialized = true;

    /// <summary>外壳在启动时读取主题用（此时还没有任何切换动作）。</summary>
    public string InitialTheme => ThemeMode;

    /// <summary>主题写库。给外壳在启动阶段补写默认值时用。</summary>
    public Result<bool> SaveTheme(string mode) => _service.SettingsSet("theme", mode);

    [RelayCommand]
    private void SetTheme(string mode) => ThemeMode = Normalize(mode);

    private static string Normalize(string? value) => value switch
    {
        "light" => "light",
        "dark" => "dark",
        _ => "system"
    };
}
