using System.Collections.ObjectModel;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SukiUI.Enums;
using WorkStation.Core;
using WorkStation.Core.Models;
using WorkStation.Core.Reports;
using WorkStation.Core.Services;

namespace WorkStation.App.ViewModels;

/// <summary>
/// 设置页。主题状态只在这里保存一份，外壳只负责把它应用到 Application.RequestedThemeVariant，
/// 避免两个 VM 各存一份而不同步。
/// </summary>
public partial class SettingsViewModel : ObservableObject
{
    /// <summary>自定义主题色在 <see cref="ThemeColorName"/> 里的取值。</summary>
    public const string CustomColorName = "Custom";

    /// <summary>字体下拉里的「默认」项：走 SukiUI 自带的那套（Quicksand + 微软雅黑回退）。</summary>
    public const string DefaultFontName = "默认";

    /// <summary>设置页用到的设置键。</summary>
    private const string ThemeColorKey = "themeColor";

    private const string ThemeColorPrimaryKey = "themeColorPrimary";

    private const string ThemeColorAccentKey = "themeColorAccent";

    private const string FontKey = "uiFont";

    /// <summary>常用字体排在列表前面，省得在一百多个系统字体里翻。</summary>
    private static readonly string[] PreferredFonts =
    [
        "Microsoft YaHei UI", "Microsoft YaHei", "微软雅黑",
        "Noto Sans SC", "思源黑体", "Source Han Sans SC",
        "DengXian", "等线", "SimHei", "黑体", "SimSun", "宋体",
        "KaiTi", "楷体", "FangSong", "仿宋",
        "Segoe UI", "Inter", "Arial", "Calibri",
        "Cascadia Mono", "Consolas"
    ];

    private readonly WorkStationService _service;
    private readonly Action<string> _applyTheme;
    private readonly Action<SukiColor> _applyThemeColor;
    private readonly Action<Color, Color>? _applyCustomThemeColor;
    private readonly Action<string>? _applyFont;
    private readonly Action<string, bool> _toast;
    private bool _initialized;
    private bool _applyingReportReminders;

    public SettingsViewModel(
        WorkStationService service,
        Action<string> applyTheme,
        Action<SukiColor> applyThemeColor,
        Action<string, bool> toast,
        Action<Color, Color>? applyCustomThemeColor = null,
        Action<string>? applyFont = null)
    {
        _service = service;
        _applyTheme = applyTheme;
        _applyThemeColor = applyThemeColor;
        _applyCustomThemeColor = applyCustomThemeColor;
        _applyFont = applyFont;
        _toast = toast;

        var saved = service.SettingsGet<string>("theme");
        _themeMode = Normalize(saved.Data);

        var savedColor = service.SettingsGet<string>(ThemeColorKey);
        _themeColorName = NormalizeColor(savedColor.Data);

        _customPrimaryColor = ParseColor(service.SettingsGet<string>(ThemeColorPrimaryKey).Data, DefaultPrimaryColor);
        _customAccentColor = ParseColor(service.SettingsGet<string>(ThemeColorAccentKey).Data, DefaultAccentColor);

        _selectedFont = NormalizeFont(service.SettingsGet<string>(FontKey).Data);

        BuildReportOptions();
        ApplyReportReminders(service.ReportRemindersGet());

        var closeToTray = service.CloseToTrayGet();
        _closeToTray = closeToTray is not { Ok: true } || closeToTray.Data;

        _dataDirectory = service.Info().UserDataPath;

        foreach (var option in ColorOptions) option.IsSelected = option.Name == _themeColorName;
    }

    [ObservableProperty]
    private string _themeMode = "system";

    public bool IsSystemTheme => ThemeMode == "system";

    public bool IsLightTheme => ThemeMode == "light";

    public bool IsDarkTheme => ThemeMode == "dark";

    // ---------- 主题色 ----------

    // SukiUI 7.0.1 只内置 4 种主题色，正好对应设置页的一排圆形色块。
    // 顺序与截图一致：橙、红、绿、蓝。
    public IReadOnlyList<ThemeColorOption> ColorOptions { get; } =
    [
        new ThemeColorOption("Orange", SukiColor.Orange, "#D48806", "橙"),
        new ThemeColorOption("Red", SukiColor.Red, "#D03A2F", "红"),
        new ThemeColorOption("Green", SukiColor.Green, "#537834", "绿"),
        new ThemeColorOption("Blue", SukiColor.Blue, "#0A59F7", "蓝")
    ];

    [ObservableProperty]
    private string _themeColorName = "Blue";

    /// <summary>选中色对应的 SukiColor，外壳据此重新计算主色阶。</summary>
    public SukiColor ThemeColorValue => ParseColor(ThemeColorName);

    /// <summary>当前是否用的是自定义配色（而不是内置四色）。</summary>
    public bool IsCustomThemeColor => ThemeColorName == CustomColorName;

    /// <summary>自定义配色的主色。</summary>
    [ObservableProperty]
    private Color _customPrimaryColor = DefaultPrimaryColor;

    /// <summary>自定义配色的强调色。</summary>
    [ObservableProperty]
    private Color _customAccentColor = DefaultAccentColor;

    public string CustomPrimaryHex => ToHex(CustomPrimaryColor);

    public string CustomAccentHex => ToHex(CustomAccentColor);

    /// <summary>主色的画刷，设置页上的预览色块直接用它。</summary>
    public IBrush CustomPrimaryBrush => new SolidColorBrush(CustomPrimaryColor);

    /// <summary>强调色的画刷。</summary>
    public IBrush CustomAccentBrush => new SolidColorBrush(CustomAccentColor);

    /// <summary>主题色区块下面那行说明：当前是内置配色还是自定义配色。</summary>
    public string CustomThemeSummary => IsCustomThemeColor
        ? $"主色 {CustomPrimaryHex} · 强调色 {CustomAccentHex}"
        : "点最后一个色块可以自定主色与强调色。";

    public string SelectedColorLabel =>
        ColorOptions.FirstOrDefault(o => o.Name == ThemeColorName)?.Label ?? "蓝";

    // ---------- 字体 ----------

    /// <summary>可选字体：第一项是「默认」，后面是系统里装着的字体。</summary>
    public IReadOnlyList<string> FontOptions { get; } = BuildFontOptions();

    [ObservableProperty]
    private string _selectedFont = DefaultFontName;

    /// <summary>字体区块下面那行说明。</summary>
    public string SelectedFontLabel => SelectedFont == DefaultFontName
        ? "默认（SukiUI 自带字体 + 微软雅黑回退）"
        : SelectedFont;

    /// <summary>预览用的字体族：跟着当前选择走，中文回退链与实际应用的一致。</summary>
    public FontFamily SelectedFontFamily => SelectedFont == DefaultFontName
        ? new FontFamily("Microsoft YaHei UI, Microsoft YaHei, Segoe UI")
        : new FontFamily($"{SelectedFont}, Microsoft YaHei UI, Microsoft YaHei, Segoe UI");

    // ---------- 自定义主题色弹窗 ----------

    /// <summary>自定义配色弹窗是否打开。窗口层监听这个状态来弹 Suki 对话框。</summary>
    [ObservableProperty]
    private bool _isCustomThemeDialogOpen;

    [RelayCommand]
    private void OpenCustomThemeDialog()
    {
        // 打开时把草稿还原成已保存的值：上一次取消掉的改动不该留在这儿。
        CustomPrimaryColor = ParseColor(_service.SettingsGet<string>(ThemeColorPrimaryKey).Data, DefaultPrimaryColor);
        CustomAccentColor = ParseColor(_service.SettingsGet<string>(ThemeColorAccentKey).Data, DefaultAccentColor);
        IsCustomThemeDialogOpen = true;
    }

    [RelayCommand]
    private void CloseCustomThemeDialog()
    {
        // 取消：草稿作废，恢复成已保存的值。
        CustomPrimaryColor = ParseColor(_service.SettingsGet<string>(ThemeColorPrimaryKey).Data, DefaultPrimaryColor);
        CustomAccentColor = ParseColor(_service.SettingsGet<string>(ThemeColorAccentKey).Data, DefaultAccentColor);
        IsCustomThemeDialogOpen = false;
    }

    [RelayCommand]
    private void ApplyCustomTheme()
    {
        var primary = CustomPrimaryColor;
        var accent = CustomAccentColor;

        var primarySaved = _service.SettingsSet(ThemeColorPrimaryKey, ToHex(primary));
        if (!primarySaved.Ok)
        {
            _toast(primarySaved.Error?.Message ?? "主题色保存失败", true);
            return;
        }

        var accentSaved = _service.SettingsSet(ThemeColorAccentKey, ToHex(accent));
        if (!accentSaved.Ok)
        {
            _toast(accentSaved.Error?.Message ?? "主题色保存失败", true);
            return;
        }

        IsCustomThemeDialogOpen = false;

        // 主题名本身也要落盘，否则下次启动还会回到内置色。
        var nameSaved = _service.SettingsSet(ThemeColorKey, CustomColorName);
        if (!nameSaved.Ok) _toast(nameSaved.Error?.Message ?? "主题色保存失败", true);

        if (ThemeColorName != CustomColorName)
        {
            // 改名会顺带触发一次应用
            ThemeColorName = CustomColorName;
            return;
        }

        // 本来就是自定义：名字没变，改名那条路不会触发，得自己套一次新配色
        ApplyCurrentColor();
    }

    /// <summary>外壳启动时调用：把保存下来的主题色套到应用上。</summary>
    public void ApplySavedColor() => ApplyCurrentColor();

    public string DatabasePath => _service.Info().DatabasePath;

    public string UserDataPath => _service.Info().UserDataPath;

    public string Version => _service.Info().Version;

    public string BackupDirectory => Path.Combine(UserDataPath, "backups");

    // ---------- 数据保存目录 ----------

    /// <summary>当前数据保存目录。更改后先指向新目录，重启才真正从那里读写。</summary>
    [ObservableProperty]
    private string _dataDirectory;

    /// <summary>
    /// 更改数据保存目录：把整库复制到新目录并写引导配置，重启后生效。
    /// 迁移逻辑在服务层（改库文件 +写 app-config.json），这里只接结果和提示。
    /// </summary>
    public void ChangeDataDirectory(string path, string? configPath = null)
    {
        var result = _service.ChangeDataDirectory(path, configPath);
        if (!result.Ok)
        {
            _toast(result.Error?.Message ?? "更改数据目录失败", true);
            return;
        }

        DataDirectory = result.Data!;
        _toast("数据目录已更改，重启应用后生效", false);
    }

    // ---------- 报告提醒 ----------

    [ObservableProperty]
    private bool _reportDailyEnabled = true;

    [ObservableProperty]
    private string _reportDailyTime = "09:00";

    [ObservableProperty]
    private bool _reportWeeklyEnabled = true;

    [ObservableProperty]
    private string _reportWeeklyTime = "17:00";

    [ObservableProperty]
    private bool _reportMonthlyEnabled = true;

    [ObservableProperty]
    private string _reportMonthlyTime = "17:00";

    [ObservableProperty]
    private bool _closeToTray;

    /// <summary>提醒时间：整点与半点最常用，避免手输 9:00 / 09:0 这类格式差异。</summary>
    public ObservableCollection<string> ReportTimeOptions { get; } = [];

    public ObservableCollection<ReportWeekdayOption> ReportWeekdayOptions { get; } = [];

    public ObservableCollection<ReportMonthDayOption> ReportMonthDayOptions { get; } = [];

    /// <summary>周报选中的星期。用选项对象而不是整数：下拉框按引用记选中项。</summary>
    [ObservableProperty]
    private ReportWeekdayOption? _selectedReportWeekday;

    /// <summary>月报选中的几号。</summary>
    [ObservableProperty]
    private ReportMonthDayOption? _selectedReportMonthDay;

    partial void OnReportDailyEnabledChanged(bool value) => SaveReportReminders();

    partial void OnReportDailyTimeChanged(string value) => SaveReportReminders();

    partial void OnReportWeeklyEnabledChanged(bool value) => SaveReportReminders();

    partial void OnReportWeeklyTimeChanged(string value) => SaveReportReminders();

    partial void OnReportMonthlyEnabledChanged(bool value) => SaveReportReminders();

    partial void OnReportMonthlyTimeChanged(string value) => SaveReportReminders();

    partial void OnSelectedReportWeekdayChanged(ReportWeekdayOption? value) => SaveReportReminders();

    partial void OnSelectedReportMonthDayChanged(ReportMonthDayOption? value) => SaveReportReminders();

    partial void OnCloseToTrayChanged(bool value)
    {
        if (!_initialized) return;

        var result = _service.CloseToTraySet(value);
        if (!result.Ok) _toast(result.Error?.Message ?? "窗口行为保存失败", true);
    }

    /// <summary>提醒的可选项：时间、星期、几号。</summary>
    private void BuildReportOptions()
    {
        ReportTimeOptions.Clear();
        for (var hour = 6; hour <= 22; hour++)
        {
            ReportTimeOptions.Add($"{hour:00}:00");
            ReportTimeOptions.Add($"{hour:00}:30");
        }

        ReportWeekdayOptions.Clear();
        var weekdayLabels = new[] { "周一", "周二", "周三", "周四", "周五", "周六", "周日" };
        for (var i = 0; i < weekdayLabels.Length; i++)
        {
            ReportWeekdayOptions.Add(new ReportWeekdayOption(i + 1, weekdayLabels[i]));
        }

        ReportMonthDayOptions.Clear();
        for (var day = 1; day <= 31; day++)
        {
            ReportMonthDayOptions.Add(new ReportMonthDayOption(day, $"{day} 号"));
        }
    }

    /// <summary>库里存的时间可能是 07:45 这类非整点半点，补进下拉否则选中项显示为空白。</summary>
    private void EnsureReportTimeOption(string? time)
    {
        if (string.IsNullOrWhiteSpace(time) || ReportTimeOptions.Contains(time)) return;
        ReportTimeOptions.Add(time);
    }

    /// <summary>把库里存的设置摊到界面属性上。</summary>
    private void ApplyReportReminders(Result<ReportReminderSettings> result)
    {
        if (result is not { Ok: true, Data: { } config }) return;

        // 读回来不是「用户改了设置」：逐个赋值会触发八次保存，甚至把半途的混合值写回库。
        _applyingReportReminders = true;
        try
        {
            EnsureReportTimeOption(config.Daily.Time);
            EnsureReportTimeOption(config.Weekly.Time);
            EnsureReportTimeOption(config.Monthly.Time);

            ReportDailyEnabled = config.Daily.Enabled;
            ReportDailyTime = config.Daily.Time;
            ReportWeeklyEnabled = config.Weekly.Enabled;
            ReportWeeklyTime = config.Weekly.Time;
            ReportMonthlyEnabled = config.Monthly.Enabled;
            ReportMonthlyTime = config.Monthly.Time;

            SelectedReportWeekday = ReportWeekdayOptions.FirstOrDefault(o => o.Value == config.Weekly.DayOfWeek);
            SelectedReportMonthDay = ReportMonthDayOptions.FirstOrDefault(o => o.Value == config.Monthly.DayOfMonth);
        }
        finally
        {
            _applyingReportReminders = false;
        }
    }

    /// <summary>保存三类报告提醒。存失败只提示，不打断设置页其它操作。</summary>
    private void SaveReportReminders()
    {
        if (!_initialized || _applyingReportReminders) return;

        var result = _service.ReportRemindersSet(new ReportReminderSettings
        {
            Daily = new ReportReminderEntry { Enabled = ReportDailyEnabled, Time = ReportDailyTime },
            Weekly = new ReportReminderEntry
            {
                Enabled = ReportWeeklyEnabled,
                Time = ReportWeeklyTime,
                DayOfWeek = SelectedReportWeekday?.Value ?? 5
            },
            Monthly = new ReportReminderEntry
            {
                Enabled = ReportMonthlyEnabled,
                Time = ReportMonthlyTime,
                DayOfMonth = SelectedReportMonthDay?.Value ?? 31
            }
        });

        if (!result.Ok)
        {
            _toast(result.Error?.Message ?? "报告提醒保存失败", true);
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

    partial void OnThemeColorNameChanged(string value)
    {
        OnPropertyChanged(nameof(SelectedColorLabel));
        OnPropertyChanged(nameof(IsCustomThemeColor));
        OnPropertyChanged(nameof(CustomThemeSummary));
        foreach (var option in ColorOptions) option.IsSelected = option.Name == value;

        if (!_initialized) return;

        var result = _service.SettingsSet(ThemeColorKey, value);
        if (!result.Ok)
        {
            _toast(result.Error?.Message ?? "主题色保存失败", true);
            return;
        }

        ApplyCurrentColor();
    }

    partial void OnCustomPrimaryColorChanged(Color value)
    {
        OnPropertyChanged(nameof(CustomPrimaryHex));
        OnPropertyChanged(nameof(CustomPrimaryBrush));
        OnPropertyChanged(nameof(CustomThemeSummary));
    }

    partial void OnCustomAccentColorChanged(Color value)
    {
        OnPropertyChanged(nameof(CustomAccentHex));
        OnPropertyChanged(nameof(CustomAccentBrush));
        OnPropertyChanged(nameof(CustomThemeSummary));
    }

    partial void OnSelectedFontChanged(string value)
    {
        OnPropertyChanged(nameof(SelectedFontLabel));
        OnPropertyChanged(nameof(SelectedFontFamily));

        if (!_initialized) return;

        var result = _service.SettingsSet(FontKey, value);
        if (!result.Ok)
        {
            _toast(result.Error?.Message ?? "字体保存失败", true);
            return;
        }

        _applyFont?.Invoke(value);
    }

    /// <summary>
    /// 把当前选中的主题色真正套到应用上。
    /// 内置四色走 SukiColor 枚举；自定义色要自己造一个 SukiColorTheme。
    /// </summary>
    private void ApplyCurrentColor()
    {
        if (IsCustomThemeColor)
        {
            _applyCustomThemeColor?.Invoke(CustomPrimaryColor, CustomAccentColor);
            return;
        }

        _applyThemeColor(ParseColor(ThemeColorName));
    }

    [RelayCommand]
    private void SetThemeColor(string name) => ThemeColorName = NormalizeColor(name);

    public void Reload()
    {
        var saved = _service.SettingsGet<string>("theme");
        ThemeMode = Normalize(saved.Data);

        var savedColor = _service.SettingsGet<string>(ThemeColorKey);
        ThemeColorName = NormalizeColor(savedColor.Data);

        CustomPrimaryColor = ParseColor(_service.SettingsGet<string>(ThemeColorPrimaryKey).Data, DefaultPrimaryColor);
        CustomAccentColor = ParseColor(_service.SettingsGet<string>(ThemeColorAccentKey).Data, DefaultAccentColor);

        SelectedFont = NormalizeFont(_service.SettingsGet<string>(FontKey).Data);

        ApplyReportReminders(_service.ReportRemindersGet());

        var closeToTray = _service.CloseToTrayGet();
        if (closeToTray is { Ok: true }) CloseToTray = closeToTray.Data;

        _initialized = true;

        // 主题色可能在别的入口被改过（比如标题栏的切换按钮），回到设置页时重新套一次。
        ApplyCurrentColor();
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

    private static string NormalizeColor(string? value) => value switch
    {
        "Green" => "Green",
        "Red" => "Red",
        "Orange" => "Orange",
        CustomColorName => CustomColorName,
        _ => "Blue"
    };

    private static SukiColor ParseColor(string? value) => value switch
    {
        "Green" => SukiColor.Green,
        "Red" => SukiColor.Red,
        "Orange" => SukiColor.Orange,
        _ => SukiColor.Blue
    };

    /// <summary>字体只认「默认」和白名单之外的一切字符串：存坏了就当默认。</summary>
    private static string NormalizeFont(string? value) =>
        string.IsNullOrWhiteSpace(value) ? DefaultFontName : value;

    private static string ToHex(Color color) => $"#{color.R:X2}{color.G:X2}{color.B:X2}";

    private static Color ParseColor(string? value, Color fallback)
    {
        if (string.IsNullOrWhiteSpace(value)) return fallback;
        return Color.TryParse(value, out var parsed) ? parsed : fallback;
    }

    /// <summary>
    /// 字体列表：第一项「默认」，接着是常用字体，最后是系统里其余字体（按名称排）。
    /// 名称来自 FontManager，与系统实际装的字体一致。
    /// </summary>
    private static IReadOnlyList<string> BuildFontOptions()
    {
        var options = new List<string> { DefaultFontName };

        var available = new List<string>();
        foreach (var family in FontManager.Current.SystemFonts)
        {
            var name = family.Name;
            if (string.IsNullOrWhiteSpace(name)) continue;
            available.Add(name);
        }

        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { DefaultFontName };

        foreach (var preferred in PreferredFonts)
        {
            var match = available.FirstOrDefault(n => string.Equals(n, preferred, StringComparison.OrdinalIgnoreCase));
            if (match is null || !used.Add(match)) continue;
            options.Add(match);
        }

        var rest = available
            .Where(n => !used.Contains(n))
            .OrderBy(n => n, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        options.AddRange(rest);
        return options;
    }

    /// <summary>自定义配色的兜底色：与内置「蓝」一致。</summary>
    private static Color DefaultPrimaryColor => Color.Parse("#0A59F7");

    /// <summary>自定义配色的兜底强调色：与内置「蓝」一致。</summary>
    private static Color DefaultAccentColor => Color.Parse("#F7A80A");
}

/// <summary>设置页里一个可选主题色：名称（持久化用）、对应 SukiColor、展示色值、中文标签。</summary>
public sealed partial class ThemeColorOption : ObservableObject
{
    public string Name { get; }
    public SukiColor Value { get; }
    public string Hex { get; }
    public string Label { get; }

    [ObservableProperty]
    private bool _isSelected;

    public ThemeColorOption(string name, SukiColor value, string hex, string label)
    {
        Name = name;
        Value = value;
        Hex = hex;
        Label = label;
    }
}
