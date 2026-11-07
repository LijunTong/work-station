using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Platform.Storage;
using SukiUI.Controls;
using WorkStation.App.ViewModels;

namespace WorkStation.App.Views;

public partial class SettingsView : UserControl
{
    public SettingsView()
    {
        InitializeComponent();

        // SukiUI 的 SettingsLayout 默认在宽度 < 1100 时把左侧「外观/提醒/数据/关于」
        // 摘要导航的宽度动画到 0（DockPanel_SizeChanged）。窗口一缩小导航就消失，
        // 这里把阈值降到最小可配置值 1，让它任何宽度都常驻；同时收窄到 240，
        // 给右侧内容留出足够空间，避免文字被挤到裁切。
        Layout.MinWidthWhetherStackSummaryShow = 1;
        Layout.StackSummaryWidth = 240;

        // SettingsLayout.Items 的 getter 初始为 null，XAML 集合语法会对它调用 Add。
        // 这里在模板应用之前一次性赋值：分组内容来自 XAML 资源，挂到 GroupBox 后会
        // 继承 SettingsViewModel 作为 DataContext，编译绑定照常生效。
        Layout.Items =
        [
            Item("外观", "AppearanceContent"),
            Item("提醒", "RemindersContent"),
            Item("数据", "DataContent"),
            Item("关于", "AboutContent")
        ];
    }

    private SettingsLayoutItem Item(string header, string resourceKey) =>
        new()
        {
            Header = header,
            Content = (Control)Resources[resourceKey]!
        };

    /// <summary>明暗卡片：点哪张就切到哪种主题；Tag 上带了 "light"/"dark"。</summary>
    private void OnThemeCardPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is Control { Tag: string mode } && DataContext is SettingsViewModel vm)
        {
            vm.SetThemeCommand.Execute(mode);
        }
    }

    /// <summary>
    /// 主题色块：点哪个色就套哪个；Tag 上是 SukiColor 的名字（Orange/Red/Green/Blue）。
    /// 最后一个是自定义：点了不是直接切色，而是打开取色器让用户挑主色与强调色。
    /// </summary>
    private void OnColorSwatchPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Control { Tag: string name } || DataContext is not SettingsViewModel vm) return;

        if (name == SettingsViewModel.CustomColorName)
        {
            vm.OpenCustomThemeDialogCommand.Execute(null);
            return;
        }

        vm.SetThemeColorCommand.Execute(name);
    }

    /// <summary>
    /// 更改数据保存目录：系统文件夹选择器 → 服务层整库复制到新目录 + 写引导配置。
    /// 选择器只能在 View 这层开（需要 TopLevel.StorageProvider），选完把路径交给 VM。
    /// </summary>
    private async void OnChangeDataDirClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is not SettingsViewModel vm) return;

        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel is null) return;

        var folders = await topLevel.StorageProvider.OpenFolderPickerAsync(
            new FolderPickerOpenOptions
            {
                Title = "选择数据保存目录",
                AllowMultiple = false
            });

        if (folders.Count == 0) return;

        var path = folders[0].TryGetLocalPath();
        if (string.IsNullOrWhiteSpace(path)) return;

        vm.ChangeDataDirectory(path);
    }
}
