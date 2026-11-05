using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Microsoft.Data.Sqlite;
using WorkStation.App.Controls;
using WorkStation.App.ViewModels;
using WorkStation.App.Views;
using WorkStation.Core.Models;
using WorkStation.Core.Services;

namespace WorkStation.Tests;

/// <summary>
/// UI 冒烟：实例化真实 XAML 与主窗口。
///
/// 这一层专门守两类问题：axaml 解析失败（含样式令牌拼错）与编译绑定路径写错。
/// 两者都只在运行时暴露，纯 ViewModel 测试发现不了。
/// </summary>
public class UiSmokeTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly WorkStationService _service;

    public UiSmokeTests()
    {
        _connection = TestDb.OpenMigrated();
        _service = new WorkStationService(_connection, ":memory:");
    }

    public void Dispose() => _connection.Dispose();

    [AvaloniaFact]
    public void ProjectsView_LoadsXaml()
    {
        var view = new ProjectsView
        {
            DataContext = new ProjectsViewModel(_service, (_, _) => { })
        };

        view.Measure(new Size(1000, 700));
        view.Arrange(new Rect(0, 0, 1000, 700));

        Assert.NotNull(view.Content);
        Assert.True(view.Bounds.Width > 0);
    }

    [AvaloniaFact]
    public void TasksView_LoadsXaml()
    {
        var vm = new TasksViewModel(_service, (_, _) => { });
        vm.Reload();

        var view = new TasksView { DataContext = vm };
        view.Measure(new Size(1000, 700));
        view.Arrange(new Rect(0, 0, 1000, 700));

        Assert.NotNull(view.Content);
    }

    [AvaloniaFact]
    public void SettingsView_LoadsXaml()
    {
        var vm = new SettingsViewModel(_service, _ => { }, (_, _) => { });

        var view = new SettingsView { DataContext = vm };
        view.Measure(new Size(1000, 700));
        view.Arrange(new Rect(0, 0, 1000, 700));

        Assert.NotNull(view.Content);
    }

    [AvaloniaFact]
    public void BoardView_LoadsXaml()
    {
        var vm = new BoardViewModel(_service, (_, _) => { });
        vm.Reload();

        var view = new BoardView { DataContext = vm };
        view.Measure(new Size(1000, 700));
        view.Arrange(new Rect(0, 0, 1000, 700));

        Assert.NotNull(view.Content);
        Assert.Equal(4, vm.Columns.Count);
    }

    [AvaloniaFact]
    public void CommandPaletteView_LoadsXaml()
    {
        var vm = new CommandPaletteViewModel(_service, []);
        vm.Open();

        var view = new CommandPaletteView { DataContext = vm };
        view.Measure(new Size(900, 600));
        view.Arrange(new Rect(0, 0, 900, 600));

        Assert.NotNull(view.Content);
    }

    [AvaloniaFact]
    public void MainWindow_ShowsBoardPage_ThenNavigates()
    {
        var vm = new MainWindowViewModel(_service);
        var window = new MainWindow(vm);

        window.Show();
        Dispatcher.UIThread.RunJobs();

        Assert.True(window.IsVisible);
        Assert.IsType<BoardViewModel>(vm.CurrentPage);
        Assert.Equal("看板", vm.PageTitle);

        vm.ShowTasksCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();

        Assert.IsType<TasksViewModel>(vm.CurrentPage);
        Assert.Equal("任务", vm.PageTitle);

        vm.ShowProjectsCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();

        Assert.IsType<ProjectsViewModel>(vm.CurrentPage);
        Assert.Equal("项目", vm.PageTitle);

        vm.ShowSettingsCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();

        Assert.IsType<SettingsViewModel>(vm.CurrentPage);
        Assert.Equal("设置", vm.PageTitle);

        window.Close();
    }

    [AvaloniaFact]
    public void MainWindow_StartupFailure_ShowsErrorState()
    {
        var vm = MainWindowViewModel.StartupFailed(new InvalidOperationException("database is locked"));
        var window = new MainWindow(vm);

        window.Show();
        Dispatcher.UIThread.RunJobs();

        Assert.True(vm.HasStartupError);
        Assert.False(vm.IsReady);
        Assert.Contains("database is locked", vm.StartupError);

        window.Close();
    }

    [AvaloniaFact]
    public void TasksPage_ProjectDropdown_StaysDeduplicated()
    {
        var first = _service.ProjectsCreate(new CreateProjectInput { Name = "P1" }).Data!;
        _service.ProjectsCreate(new CreateProjectInput { Name = "P2" });

        var vm = new MainWindowViewModel(_service);
        var window = new MainWindow(vm);
        window.Show();
        Dispatcher.UIThread.RunJobs();

        vm.ShowTasksCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();

        // 切换筛选会重建选项列表：Clear() 时 ComboBox 会把 SelectedItem 写回 null，
        // 若没做重入保护就会多跑一遍重建，下拉里每个项目出现两次。
        vm.Tasks!.SelectedProject = vm.Tasks.ProjectOptions.First(o => o.Id == first.Id);
        Dispatcher.UIThread.RunJobs();
        vm.Tasks.SelectedProject = vm.Tasks.ProjectOptions[0];
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(
            ["全部项目", "P1", "P2"],
            vm.Tasks.ProjectOptions.Select(o => o.Name).ToArray());

        vm.Tasks.NewTaskCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(
            ["独立任务", "P1", "P2"],
            vm.Tasks.Form!.ProjectChoices.Select(o => o.Name).ToArray());

        window.Close();
    }

    /// <summary>无头宿主下发一次按键。PhysicalKey 只用于输入法映射，这里传 None。</summary>
    private static void Press(Window window, Key key, RawInputModifiers modifiers = RawInputModifiers.None) =>
        window.KeyPress(key, modifiers, PhysicalKey.None, null);

    [AvaloniaFact]
    public void Keyboard_Shortcuts_NavigateAndTogglePalette()
    {
        var vm = new MainWindowViewModel(_service);
        var window = new MainWindow(vm);
        window.Show();
        Dispatcher.UIThread.RunJobs();

        // 数字键切页
        Press(window, Key.D2);
        Dispatcher.UIThread.RunJobs();
        Assert.IsType<TasksViewModel>(vm.CurrentPage);

        Press(window, Key.D3);
        Dispatcher.UIThread.RunJobs();
        Assert.IsType<ProjectsViewModel>(vm.CurrentPage);

        // Ctrl+K 打开、Esc 关闭
        Press(window, Key.K, RawInputModifiers.Control);
        Dispatcher.UIThread.RunJobs();
        Assert.True(vm.Palette!.IsOpen);

        Press(window, Key.Escape);
        Dispatcher.UIThread.RunJobs();
        Assert.False(vm.Palette.IsOpen);

        window.Close();
    }

    /// <summary>
    /// 图标按钮的两条硬规则：里面必须真的有一个图标，且必须带 tooltip。
    /// 图标本身不承载文字，没有 tooltip 的图标按钮等于一个没有名字的控件。
    /// </summary>
    [AvaloniaFact]
    public void IconButtons_HaveGlyphAndTooltip()
    {
        var project = _service.ProjectsCreate(new CreateProjectInput { Name = "验收项目" }).Data!;
        _service.TasksCreate(new CreateTaskInput { Title = "验收任务", ProjectId = project.Id });

        var vm = new MainWindowViewModel(_service);
        var window = new MainWindow(vm) { Width = 1280, Height = 820 };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var problems = new List<string>();

        void Inspect(string page)
        {
            window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();

            foreach (var button in window.GetVisualDescendants().OfType<Button>()
                         .Where(b => b.Classes.Contains("icon") && b.IsVisible))
            {
                var tip = ToolTip.GetTip(button) as string;
                if (string.IsNullOrWhiteSpace(tip))
                {
                    problems.Add($"{page}: 图标按钮缺少 tooltip（位于 {button.TranslatePoint(default, window)}）");
                }

                // 内容可能还没被 ContentPresenter 实例化（例如抽屉还没打开），
                // 所以同时认 Content 本身
                if (button.Content is not Icon && !button.GetVisualDescendants().OfType<Icon>().Any())
                {
                    problems.Add($"{page}: 图标按钮里没有 Icon");
                }
            }
        }

        Inspect("看板");
        vm.Board!.SwitchBoardCommand.Execute(nameof(BoardMode.Projects));
        Inspect("项目看板");

        vm.ShowTasksCommand.Execute(null);
        Inspect("任务");

        vm.ShowProjectsCommand.Execute(null);
        Inspect("项目");

        Assert.Empty(problems);
        window.Close();
    }

    /// <summary>
    /// 合成框里「占位文字」和「新建按钮」必须落在同一条中心线上。
    ///
    /// TextBox 的 TextPresenter 默认会被 MinHeight 撑满整个输入框高度，而文字是贴着
    /// 行框顶部绘制的，于是占位文字比同一行的按钮文字高 2.5px——肉眼就是「没对齐」。
    /// 这里同时守住「中心线一致」和「行框没有被撑满」两件事。
    /// </summary>
    [AvaloniaFact]
    public void Composer_TextAndButtonShareOneCenterLine()
    {
        var window = new MainWindow(new MainWindowViewModel(_service)) { Width = 1280, Height = 820 };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();

        var composer = window.GetVisualDescendants().OfType<Border>().First(b => b.Classes.Contains("composer"));
        var box = composer.GetVisualDescendants().OfType<TextBox>().First();
        var presenter = box.GetVisualDescendants().OfType<TextPresenter>().First();
        var content = composer.GetVisualDescendants().OfType<Button>().First()
            .GetVisualDescendants().OfType<ContentPresenter>().First();

        double Center(Control control) =>
            control.TranslatePoint(default, composer)!.Value.Y + control.Bounds.Height / 2;

        Assert.InRange(Math.Abs(Center(presenter) - Center(content)), 0, 1.0);
        Assert.True(
            presenter.Bounds.Height < box.Bounds.Height,
            $"文字行被撑满输入框高度（{presenter.Bounds.Height} vs {box.Bounds.Height}），文字会贴顶绘制");

        window.Close();
    }

    [AvaloniaFact]
    public void Keyboard_N_FocusesQuickAdd()
    {
        var vm = new MainWindowViewModel(_service);
        var window = new MainWindow(vm);
        window.Show();
        Dispatcher.UIThread.RunJobs();

        Press(window, Key.N);
        Dispatcher.UIThread.RunJobs();

        var box = window.FindControl<TextBox>("QuickAddBox");
        Assert.Same(box, window.FocusManager?.GetFocusedElement());

        window.Close();
    }

    [AvaloniaFact]
    public void Board_TaskCard_DoubleClick_OpensEditor()
    {
        var task = _service.TasksCreate(new CreateTaskInput { Title = "双击我" }).Data!;

        var vm = new MainWindowViewModel(_service);
        var window = new MainWindow(vm);
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var card = window.GetVisualDescendants()
            .OfType<Border>()
            .First(b => b.Classes.Contains("board-card")
                        && b.DataContext is BoardTaskCardViewModel c
                        && c.Task.Id == task.Id);

        var center = card.TranslatePoint(new Point(card.Bounds.Width / 2, card.Bounds.Height / 2), window);
        Assert.NotNull(center);

        // 两次快速按下-抬起：卡片按下时不再立即开拖拽会话，双击才到得了
        for (var i = 0; i < 2; i++)
        {
            window.MouseDown(center!.Value, MouseButton.Left, RawInputModifiers.None);
            window.MouseUp(center.Value, MouseButton.Left, RawInputModifiers.None);
        }

        Dispatcher.UIThread.RunJobs();

        Assert.Equal(AppPage.Tasks, vm.Page);
        Assert.True(vm.Tasks!.IsFormOpen, "双击卡片应打开编辑抽屉");

        window.Close();
    }

    [AvaloniaFact]
    public void PaletteNewProject_RendersProjectDrawer()
    {
        var vm = new MainWindowViewModel(_service);
        var window = new MainWindow(vm);
        window.Show();
        Dispatcher.UIThread.RunJobs();

        vm.OpenPaletteCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();

        vm.Palette!.Results.Single(r => r.Title == "新建项目" && r.Hint == "动作")
            .ActivateCommand.Execute(null);

        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();

        // 抽屉必须真的长出来：只断言 IsFormOpen 会漏掉「表单为空 / 高度塌成 0」
        var drawer = window.GetVisualDescendants()
            .OfType<Border>()
            .FirstOrDefault(b => b.Classes.Contains("drawer"));

        Assert.NotNull(drawer);
        Assert.True(drawer!.IsVisible, "抽屉应可见");
        Assert.True(drawer.Bounds.Width > 300, $"抽屉宽度异常：{drawer.Bounds}");
        Assert.True(drawer.Bounds.Height > 300, $"抽屉高度异常：{drawer.Bounds}");

        window.Close();
    }

    [AvaloniaFact]
    public void PaletteNewProject_ClickedByMouse_OpensDrawer()
    {
        var vm = new MainWindowViewModel(_service);
        var window = new MainWindow(vm);
        window.Show();
        Dispatcher.UIThread.RunJobs();

        vm.OpenPaletteCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();

        var row = window.GetVisualDescendants()
            .OfType<Button>()
            .First(b => b.DataContext is PaletteItem { Title: "新建项目" });

        var center = row.TranslatePoint(new Point(row.Bounds.Width / 2, row.Bounds.Height / 2), window);
        Assert.NotNull(center);

        window.MouseDown(center!.Value, MouseButton.Left, RawInputModifiers.None);
        window.MouseUp(center.Value, MouseButton.Left, RawInputModifiers.None);
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(AppPage.Projects, vm.Page);
        Assert.True(vm.Projects!.IsFormOpen, "点「新建项目」应打开项目抽屉");
        Assert.False(vm.Palette!.IsOpen, "执行后面板应关闭");

        window.Close();
    }

    [AvaloniaFact]
    public void Palette_ArrowDown_KeepsSelectionVisible()
    {
        for (var i = 0; i < 20; i++)
        {
            _service.TasksCreate(new CreateTaskInput { Title = $"任务 {i:D2}" });
        }

        var vm = new MainWindowViewModel(_service);
        var window = new MainWindow(vm);
        window.Show();
        Dispatcher.UIThread.RunJobs();

        vm.OpenPaletteCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();

        var palette = window.GetVisualDescendants().OfType<CommandPaletteView>().Single();
        var scroller = palette.FindControl<ScrollViewer>("ResultScroller");
        Assert.NotNull(scroller);

        // 面板里除了 8 个动作还有 6 个任务，向下走到底必须把列表带着滚
        for (var i = 0; i < 12; i++)
        {
            Press(window, Key.Down);
            Dispatcher.UIThread.RunJobs();
        }

        Assert.True(
            scroller!.Offset.Y > 0,
            $"选中项移出可视区时列表应跟随滚动，实际 Offset.Y={scroller.Offset.Y}");

        window.Close();
    }

    [AvaloniaFact]
    public void Theme_Switch_AppliesVariantAndPersists()
    {
        var vm = new MainWindowViewModel(_service);

        vm.Settings!.SetThemeCommand.Execute("dark");

        Assert.Equal("dark", vm.Settings.ThemeMode);
        Assert.Equal("dark", _service.SettingsGet<string>("theme").Data);

        vm.Settings.SetThemeCommand.Execute("light");

        Assert.Equal("light", _service.SettingsGet<string>("theme").Data);
    }
}
