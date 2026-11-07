using Avalonia;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Microsoft.Data.Sqlite;
using SukiUI;
using WorkStation.App.ViewModels;
using WorkStation.App.Views;
using WorkStation.Core.Services;

namespace WorkStation.Tests;

/// <summary>
/// 自定义主题色与界面字体。
///
/// 这两块的坑都在「切换之后能不能撑住」：
///   - 自定义色在明暗切换后会被打回内置色（SukiTheme.ThemeColor 停在枚举值上）；
///   - 字体只换一半（SukiUI 的正文控件和窗口级浮层各读一个资源键）。
/// 所以测试都盯住「切完之后资源里到底是什么」。
/// </summary>
public class ThemeCustomizationTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly WorkStationService _service;

    public ThemeCustomizationTests()
    {
        _connection = TestDb.OpenMigrated();
        _service = new WorkStationService(_connection, ":memory:");
    }

    public void Dispose() => _connection.Dispose();

    [AvaloniaFact]
    public void CustomThemeColor_PersistsAndInvokesCallback()
    {
        (Color primary, Color accent)? applied = null;
        var vm = new SettingsViewModel(
            _service,
            _ => { },
            _ => { },
            (_, _) => { },
            (p, a) => applied = (p, a));
        vm.MarkInitialized();

        vm.CustomPrimaryColor = Color.Parse("#123456");
        vm.CustomAccentColor = Color.Parse("#AABBCC");
        vm.ApplyCustomThemeCommand.Execute(null);

        Assert.Equal(SettingsViewModel.CustomColorName, vm.ThemeColorName);
        Assert.True(vm.IsCustomThemeColor);
        Assert.Equal("#123456", _service.SettingsGet<string>("themeColorPrimary").Data);
        Assert.Equal("#AABBCC", _service.SettingsGet<string>("themeColorAccent").Data);
        Assert.NotNull(applied);
        Assert.Equal(Color.Parse("#123456"), applied!.Value.primary);
        Assert.Equal(Color.Parse("#AABBCC"), applied.Value.accent);
    }

    [AvaloniaFact]
    public void CustomThemeColor_CancelKeepsSavedValues()
    {
        var vm = new SettingsViewModel(_service, _ => { }, _ => { }, (_, _) => { });
        vm.MarkInitialized();

        vm.CustomPrimaryColor = Color.Parse("#123456");
        vm.CustomAccentColor = Color.Parse("#AABBCC");
        vm.OpenCustomThemeDialogCommand.Execute(null);
        Assert.True(vm.IsCustomThemeDialogOpen);

        // 弹窗里改了一通再取消：草稿作废，已保存的值不动
        vm.CustomPrimaryColor = Color.Parse("#654321");
        vm.CloseCustomThemeDialogCommand.Execute(null);

        Assert.False(vm.IsCustomThemeDialogOpen);
        Assert.Equal(Color.Parse("#0A59F7"), vm.CustomPrimaryColor);
    }

    [AvaloniaFact]
    public void CustomColorTheme_SurvivesLightDarkSwitch()
    {
        var vm = new MainWindowViewModel(_service);
        var window = new MainWindow(vm);
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var app = Application.Current!;
        var theme = SukiTheme.GetInstance(app);
        var previous = app.RequestedThemeVariant;

        try
        {
            vm.ApplyCustomThemeColor(Color.Parse("#123456"), Color.Parse("#AABBCC"));

            Assert.Equal(Color.Parse("#123456"), theme.ActiveColorTheme!.Primary);
            Assert.Equal(Color.Parse("#123456"), (Color)app.Resources["SukiPrimaryColor"]!);
            Assert.Equal(Color.Parse("#AABBCC"), (Color)app.Resources["SukiAccentColor"]!);

            // 切到深色：自定义色必须还在
            app.RequestedThemeVariant = ThemeVariant.Dark;
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();

            Assert.Equal(Color.Parse("#123456"), theme.ActiveColorTheme!.Primary);
            Assert.Equal(Color.Parse("#123456"), (Color)app.Resources["SukiPrimaryColor"]!);

            // 再切回浅色，也一样
            app.RequestedThemeVariant = ThemeVariant.Light;
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();

            Assert.Equal(Color.Parse("#123456"), theme.ActiveColorTheme!.Primary);
            Assert.Equal(Color.Parse("#123456"), (Color)app.Resources["SukiPrimaryColor"]!);
        }
        finally
        {
            app.RequestedThemeVariant = previous;
            vm.ApplyThemeColor(SukiUI.Enums.SukiColor.Blue);
            window.Close();
            Dispatcher.UIThread.RunJobs();
        }
    }

    [AvaloniaFact]
    public void SwitchingBackToBuiltInColor_ReplacesCustomTheme()
    {
        var vm = new MainWindowViewModel(_service);
        var window = new MainWindow(vm);
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var app = Application.Current!;
        var theme = SukiTheme.GetInstance(app);

        try
        {
            vm.ApplyCustomThemeColor(Color.Parse("#123456"), Color.Parse("#AABBCC"));
            Assert.Equal(Color.Parse("#123456"), theme.ActiveColorTheme!.Primary);

            // 切回内置蓝：自定义色必须真的退掉。
            // 这里最容易出的问题是 SukiUI 的枚举重载靠 ThemeColor 属性变化触发，
            // 而自定义主题不改 ThemeColor，同一个内置值再点一次就不会触发换色。
            vm.ApplyThemeColor(SukiUI.Enums.SukiColor.Blue);

            Assert.Equal(Color.Parse("#0A59F7"), theme.ActiveColorTheme!.Primary);
            Assert.Equal(Color.Parse("#0A59F7"), (Color)app.Resources["SukiPrimaryColor"]!);
            Assert.Equal(Color.Parse("#F7A80A"), (Color)app.Resources["SukiAccentColor"]!);
        }
        finally
        {
            vm.ApplyThemeColor(SukiUI.Enums.SukiColor.Blue);
            window.Close();
            Dispatcher.UIThread.RunJobs();
        }
    }

    [AvaloniaFact]
    public void FontSelection_PersistsAndApplies()
    {
        string? applied = null;
        var vm = new SettingsViewModel(
            _service,
            _ => { },
            _ => { },
            (_, _) => { },
            null,
            name => applied = name);
        vm.MarkInitialized();

        vm.SelectedFont = "Segoe UI";

        Assert.Equal("Segoe UI", applied);
        Assert.Equal("Segoe UI", _service.SettingsGet<string>("uiFont").Data);
    }

    [AvaloniaFact]
    public void ApplyFont_OverridesBothFontResources_AndRestores()
    {
        var vm = new MainWindowViewModel(_service);
        var app = Application.Current!;

        try
        {
            vm.ApplyFont("Segoe UI");

            // SukiUI 正文控件读的 DefaultFontFamily，和窗口级字体，两个都要被覆盖
            var defaultFont = (FontFamily?)app.Resources["DefaultFontFamily"];
            var windowFont = (FontFamily?)app.Resources["UiFontFamily"];
            Assert.NotNull(defaultFont);
            Assert.NotNull(windowFont);
            Assert.Contains("Segoe UI", defaultFont!.FamilyNames);
            Assert.Contains("Segoe UI", windowFont!.FamilyNames);
            // 中文回退链必须留着，否则汉字会掉回默认字体
            Assert.Contains("Microsoft YaHei UI", defaultFont.FamilyNames);

            // 选「默认」：把 DefaultFontFamily 撤掉，退回 SukiUI 自带的那套
            vm.ApplyFont(SettingsViewModel.DefaultFontName);
            Assert.False(app.Resources.ContainsKey("DefaultFontFamily"));
        }
        finally
        {
            vm.ApplyFont(SettingsViewModel.DefaultFontName);
        }
    }

    [AvaloniaFact]
    public void FontOptions_StartWithDefaultAndContainSystemFonts()
    {
        var vm = new SettingsViewModel(_service, _ => { }, _ => { }, (_, _) => { });

        Assert.Equal(SettingsViewModel.DefaultFontName, vm.FontOptions[0]);
        Assert.Equal(SettingsViewModel.DefaultFontName, vm.SelectedFont);
        Assert.True(vm.FontOptions.Count > 10);
    }
}
