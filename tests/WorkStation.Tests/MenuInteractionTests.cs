using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Microsoft.Data.Sqlite;
using SukiUI.Controls;
using WorkStation.App.ViewModels;
using WorkStation.App.Views;
using WorkStation.Core.Services;

namespace WorkStation.Tests;

/// <summary>
/// 侧边栏交互：按下只记录、松开才切页；拖拽取消；折叠状态持久化；
/// 折叠后补 Tooltip；Ctrl 组合键与菜单焦点回车。
/// </summary>
public class MenuInteractionTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly WorkStationService _service;

    public MenuInteractionTests()
    {
        _connection = TestDb.OpenMigrated();
        _service = new WorkStationService(_connection, ":memory:");
    }

    public void Dispose() => _connection.Dispose();

    private (MainWindowViewModel Vm, MainWindow Window) Show()
    {
        var vm = new MainWindowViewModel(_service);
        var window = new MainWindow(vm) { Width = 1280, Height = 820 };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        return (vm, window);
    }

    private static Point CenterOf(SukiSideMenuItem item, Window window) =>
        item.TranslatePoint(new Point(item.Bounds.Width / 2, item.Bounds.Height / 2), window)!.Value;

    private static SukiSideMenuItem Item(Window window, string tag) =>
        window.GetVisualDescendants().OfType<SukiSideMenuItem>().Single(i => i.Tag as string == tag);

    private static void Press(Window window, Key key, RawInputModifiers modifiers = RawInputModifiers.None) =>
        window.KeyPress(key, modifiers, PhysicalKey.None, null);

    /// <summary>按下只高亮，不切页；松开才导航。</summary>
    [AvaloniaFact]
    public void PressWithoutRelease_DoesNotNavigate()
    {
        var (vm, window) = Show();
        var point = CenterOf(Item(window, "Projects"), window);

        window.MouseDown(point, MouseButton.Left, RawInputModifiers.None);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(AppPage.Dashboard, vm.Page);

        window.MouseUp(point, MouseButton.Left, RawInputModifiers.None);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(AppPage.Projects, vm.Page);
        window.Close();
    }

    /// <summary>按下和松开之间位移超过 6px，视为拖动滚动，不能切页。</summary>
    [AvaloniaFact]
    public void DragBeyondSlop_DoesNotNavigate()
    {
        var (vm, window) = Show();
        var point = CenterOf(Item(window, "Projects"), window);
        var dragged = new Point(point.X, point.Y + 30);

        window.MouseDown(point, MouseButton.Left, RawInputModifiers.None);
        window.MouseUp(dragged, MouseButton.Left, RawInputModifiers.None);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(AppPage.Dashboard, vm.Page);
        window.Close();
    }

    /// <summary>折叠后外壳左列收到 48，内容区跟着左移；Tooltip 补上页面名；状态写入设置。</summary>
    [AvaloniaFact]
    public void SidebarCollapse_MovesContentPersistsAndAddsToolTip()
    {
        var (vm, window) = Show();
        var menu = window.FindControl<SukiSideMenu>("SideMenu")!;
        var shell = window.FindControl<Grid>("ShellGrid")!;
        var dashboard = Item(window, "Dashboard");

        Assert.True(vm.IsSidebarExpanded);
        Assert.Equal(236, shell.ColumnDefinitions[0].Width.Value);
        Assert.Null(ToolTip.GetTip(dashboard));

        vm.ToggleSidebarCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();

        Assert.False(vm.IsSidebarExpanded);
        Assert.False(menu.IsMenuExpanded);
        Assert.Equal(48, shell.ColumnDefinitions[0].Width.Value);
        Assert.Equal("仪表盘", ToolTip.GetTip(dashboard) as string);

        // 新开一个外壳，折叠状态应从设置里恢复
        var restored = new MainWindowViewModel(_service);
        Assert.False(restored.IsSidebarExpanded);

        window.Close();
    }

    [AvaloniaFact]
    public void CtrlB_TogglesSidebar()
    {
        var (vm, window) = Show();

        Press(window, Key.B, RawInputModifiers.Control);
        Dispatcher.UIThread.RunJobs();
        Assert.False(vm.IsSidebarExpanded);

        Press(window, Key.B, RawInputModifiers.Control);
        Dispatcher.UIThread.RunJobs();
        Assert.True(vm.IsSidebarExpanded);

        window.Close();
    }

    /// <summary>
    /// 折叠按钮在 SukiSideMenu 模板里，单向绑定不会自动回写 ViewModel；
    /// 真实点它必须走控件属性变化 → VM → 持久化这条链路。
    /// </summary>
    [AvaloniaFact]
    public void SidebarToggleButton_ClickUpdatesViewModel()
    {
        var (vm, window) = Show();
        var toggle = window.GetVisualDescendants().OfType<Button>()
            .Single(b => b.Name == "PART_SidebarToggleButton");
        var point = toggle.TranslatePoint(new Point(toggle.Bounds.Width / 2, toggle.Bounds.Height / 2), window)!.Value;

        window.MouseDown(point, MouseButton.Left, RawInputModifiers.None);
        window.MouseUp(point, MouseButton.Left, RawInputModifiers.None);
        Dispatcher.UIThread.RunJobs();

        Assert.False(vm.IsSidebarExpanded);
        window.Close();
    }

    [AvaloniaFact]
    public void CtrlDigits_NavigatePages()
    {
        var (vm, window) = Show();

        Press(window, Key.D1, RawInputModifiers.Control);
        Dispatcher.UIThread.RunJobs();
        Assert.IsType<DashboardViewModel>(vm.CurrentPage);

        Press(window, Key.D3, RawInputModifiers.Control);
        Dispatcher.UIThread.RunJobs();
        Assert.IsType<TasksViewModel>(vm.CurrentPage);

        Press(window, Key.D6, RawInputModifiers.Control);
        Dispatcher.UIThread.RunJobs();
        Assert.IsType<ReportsViewModel>(vm.CurrentPage);

        Press(window, Key.D7, RawInputModifiers.Control);
        Dispatcher.UIThread.RunJobs();
        Assert.IsType<SettingsViewModel>(vm.CurrentPage);

        window.Close();
    }

    /// <summary>键盘把焦点移到菜单项后，回车等同于点击。</summary>
    [AvaloniaFact]
    public void FocusedMenuItem_EnterNavigates()
    {
        var (vm, window) = Show();
        var tasks = Item(window, "Tasks");

        tasks.Focus();
        Dispatcher.UIThread.RunJobs();

        Press(window, Key.Enter);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(AppPage.Tasks, vm.Page);
        window.Close();
    }
}
