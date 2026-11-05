using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WorkStation.Core;
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
