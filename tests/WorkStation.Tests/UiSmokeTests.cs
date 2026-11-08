using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Microsoft.Data.Sqlite;
using SukiUI.Controls;
using WorkStation.App.Controls;
using WorkStation.App.ViewModels;
using WorkStation.App.Views;
using WorkStation.Core.Board;
using WorkStation.Core.Dashboard;
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
        var vm = new SettingsViewModel(_service, _ => { }, _ => { }, (_, _) => { });

        var view = new SettingsView { DataContext = vm };
        view.Measure(new Size(1000, 700));
        view.Arrange(new Rect(0, 0, 1000, 700));

        Assert.NotNull(view.Content);
    }

    [AvaloniaFact]
    public void SettingsView_AppearanceHasThemeCardsAndColorSwatches()
    {
        // SettingsLayout 只实例化「选中分组」的内容，未选中分组不进可视树；
        // 直接检查 XAML 解析后的资源对象（逻辑树），验证外观区块的结构没写错。
        var vm = new SettingsViewModel(_service, _ => { }, _ => { }, (_, _) => { });
        var view = new SettingsView { DataContext = vm };

        var appearance = (Control)view.Resources["AppearanceContent"]!;
        var borders = appearance.GetLogicalDescendants().OfType<Border>().ToList();
        Assert.Equal(2, borders.Count(b => b.Classes.Contains("theme-card")));
        // 四个内置色块 + 一个自定义色块
        Assert.Equal(5, borders.Count(b => b.Classes.Contains("swatch")));
    }

    [AvaloniaFact]
    public void SettingsView_RemindersUsesSwitchNotCheckbox()
    {
        var vm = new SettingsViewModel(_service, _ => { }, _ => { }, (_, _) => { });
        var view = new SettingsView { DataContext = vm };

        var reminders = (Control)view.Resources["RemindersContent"]!;
        var descendants = reminders.GetLogicalDescendants().ToList();

        // 「每日站会提醒」和「关窗驻留托盘」两处都用 Switch（ToggleSwitch），不再用 CheckBox
        Assert.Equal(2, descendants.OfType<ToggleSwitch>().Count());
        Assert.Empty(descendants.OfType<CheckBox>());
    }

    [AvaloniaFact]
    public void ThemeColorSwitch_PersistsAndRaisesCallback()
    {
        SukiUI.Enums.SukiColor? applied = null;
        var vm = new SettingsViewModel(_service, _ => { }, c => applied = c, (_, _) => { });
        vm.MarkInitialized();

        vm.SetThemeColorCommand.Execute("Red");

        Assert.Equal("Red", vm.ThemeColorName);
        Assert.Equal(SukiUI.Enums.SukiColor.Red, vm.ThemeColorValue);
        Assert.Equal(SukiUI.Enums.SukiColor.Red, applied);
        Assert.True(vm.ColorOptions.Single(o => o.Name == "Red").IsSelected);
        Assert.Equal("Red", _service.SettingsGet<string>("themeColor").Data);
    }

    [AvaloniaFact]
    public void ThemeColor_DefaultsToBlue()
    {
        var vm = new SettingsViewModel(_service, _ => { }, _ => { }, (_, _) => { });

        Assert.Equal("Blue", vm.ThemeColorName);
        Assert.Equal(4, vm.ColorOptions.Count);
        Assert.Contains(vm.ColorOptions, o => o.Name == "Orange");
        Assert.True(vm.ColorOptions.Single(o => o.Name == "Blue").IsSelected);
    }

    /// <summary>
    /// SukiUI 的 SettingsLayout 默认在宽度 &lt; 1100 时把左侧摘要导航宽度动画到 0。
    /// 页面一缩小「外观/提醒/数据/关于」就整块消失，这里锁死阈值并验证窄布局下仍可见。
    /// </summary>
    /// <summary>
    /// 主窗口内容区早期多套了一层 Grid 且没写 Grid.Row，内层落在 Auto 行拿到无限高度，
    /// SettingsLayout 的 ScrollViewer 永远不需要滚动，内容直接被窗口裁掉。
    /// 这里验证内容溢出时 Viewport 小于 Extent、且 Offset 能真正滚下去。
    /// </summary>
    /// <summary>任务行必须有可见的行外观；样式层曾整层删除，导致行看起来只是散落的文字。</summary>
    [AvaloniaFact]
    public void TaskRows_HaveVisibleRowChrome()
    {
        _service.TasksCreate(new CreateTaskInput { Title = "行样式验证" });

        var vm = new MainWindowViewModel(_service);
        var window = new MainWindow(vm) { Width = 1280, Height = 820 };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        vm.ShowTasksCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();

        var row = window.GetVisualDescendants().OfType<Border>()
            .FirstOrDefault(b => b.Classes.Contains("card-row"));
        Assert.NotNull(row);
        Assert.NotNull(row!.Background);
        // 数据行不再是一张带四边框的小卡：只有一条底部发丝线，靠它和下一行分开。
        Assert.True(row.BorderThickness.Bottom > 0, "数据行缺少底部发丝线");
        Assert.Equal(0, row.BorderThickness.Top);
        Assert.True(row.Padding.Top > 0, "数据行缺少内边距");

        window.Close();
    }

    /// <summary>四种状态列必须用彼此不同的颜色，颜色本身仍由 Suki 主题资源提供。</summary>
    [AvaloniaFact]
    public void BoardColumns_UseDistinctStatusColors()
    {
        var vm = new MainWindowViewModel(_service);
        vm.ShowBoardCommand.Execute(null);   // 启动页是仪表盘
        var window = new MainWindow(vm) { Width = 1280, Height = 820 };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();

        var columns = window.GetVisualDescendants().OfType<GlassCard>()
            .Where(c => c.Classes.Contains("column") && c.DataContext is BoardColumnViewModel)
            .ToList();
        Assert.Equal(4, columns.Count);

        var colors = columns
            .Select(c => ((BoardColumnViewModel)c.DataContext!).StatusBrush)
            .OfType<Avalonia.Media.SolidColorBrush>()
            .Select(b => b.Color)
            .Distinct()
            .ToList();
        Assert.Equal(4, colors.Count);

        window.Close();
    }

    /// <summary>看板项目卡的入口箭头固定在右上角，不再吊在卡片左下角占一行。</summary>
    [AvaloniaFact]
    public void BoardProjectCard_ArrowSitsTopRight()
    {
        var project = _service.ProjectsCreate(new CreateProjectInput { Name = "官网改版" }).Data!;
        _service.TasksCreate(new CreateTaskInput { Title = "卡内任务", ProjectId = project.Id });

        var vm = new MainWindowViewModel(_service);
        vm.ShowBoardCommand.Execute(null);   // 启动页是仪表盘
        var window = new MainWindow(vm) { Width = 1280, Height = 820 };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        vm.Board!.SwitchBoardCommand.Execute("Projects");
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();

        var card = window.GetVisualDescendants().OfType<GlassCard>()
            .First(c => c.Classes.Contains("board-card") && c.DataContext is BoardProjectCardViewModel);
        var button = card.GetVisualDescendants().OfType<Button>().Single();

        var topLeft = button.TranslatePoint(default, card);
        var bottomRight = button.TranslatePoint(new Point(button.Bounds.Width, button.Bounds.Height), card);
        Assert.NotNull(topLeft);
        Assert.NotNull(bottomRight);

        Assert.True(bottomRight!.Value.Y < card.Bounds.Height * 0.45,
            $"箭头应落在卡片上半部，实际底边 Y={bottomRight.Value.Y} 卡高={card.Bounds.Height}");
        Assert.True(bottomRight.Value.X > card.Bounds.Width * 0.7,
            $"箭头应贴近右缘，实际右缘 X={bottomRight.Value.X} 卡宽={card.Bounds.Width}");

        window.Close();
    }

    /// <summary>项目页卡片也要能看该项目的任务列表：操作区里有「查看任务」入口且真能带筛选跳转。</summary>
    [AvaloniaFact]
    public void ProjectsCard_HasViewTasksEntry()
    {
        var project = _service.ProjectsCreate(new CreateProjectInput { Name = "验收项目" }).Data!;
        _service.TasksCreate(new CreateTaskInput { Title = "项目内任务", ProjectId = project.Id });

        var vm = new MainWindowViewModel(_service);
        var window = new MainWindow(vm) { Width = 1280, Height = 820 };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        vm.ShowProjectsCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();

        var button = window.GetVisualDescendants().OfType<Button>()
            .FirstOrDefault(b => (ToolTip.GetTip(b) as string) == "查看该项目的任务列表");
        Assert.NotNull(button);

        button!.Command!.Execute(null);
        Dispatcher.UIThread.RunJobs();

        // 现在「查看任务」是弹窗，不再跳转到任务页
        Assert.Equal(AppPage.Projects, vm.Page);
        Assert.NotNull(vm.ProjectTasksDialog);
        Assert.Equal(project.Id, vm.ProjectTasksDialog!.ProjectId);
        Assert.NotNull(vm.ProjectTasksDialog.Card);

        window.Close();
    }

    [AvaloniaFact]
    public void SettingsPage_CanScrollVertically()
    {
        var vm = new MainWindowViewModel(_service);
        var window = new MainWindow(vm) { Width = 1024, Height = 640 };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        vm.ShowSettingsCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();

        var layout = window.GetVisualDescendants().OfType<SettingsLayout>().Single();
        var scroll = layout.GetVisualDescendants().OfType<ScrollViewer>().First();

        Assert.True(scroll.Extent.Height > scroll.Viewport.Height,
            $"设置页内容没有超出可视区，无法验证滚动：extent={scroll.Extent.Height} viewport={scroll.Viewport.Height}");

        scroll.Offset = new Vector(0, 120);
        Dispatcher.UIThread.RunJobs();
        Assert.True(scroll.Offset.Y > 0, "设置页滚不动");

        window.Close();
    }

    [AvaloniaFact]
    public void SettingsView_KeepsSideNavigation_WhenNarrow()
    {
        var vm = new MainWindowViewModel(_service);
        var window = new MainWindow(vm) { Width = 1024, Height = 700 };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        vm.ShowSettingsCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs(DispatcherPriority.Background);

        var layout = window.GetVisualDescendants().OfType<SettingsLayout>().Single();
        Assert.Equal(1, layout.MinWidthWhetherStackSummaryShow);
        Assert.Equal(240, layout.StackSummaryWidth);

        var summary = layout.GetVisualDescendants().OfType<StackPanel>()
            .FirstOrDefault(p => p.Name == "StackSummary");
        Assert.NotNull(summary);
        Assert.True(summary!.Bounds.Width > 0, $"窄宽度下摘要导航被隐藏：{summary.Bounds.Width}");

        window.Close();
    }

    [AvaloniaFact]
    public void RemindersView_LoadsXaml_WithTwoGroups()
    {
        var fired = _service.RemindersCreate(new CreateReminderInput
        {
            Title = "该处理了",
            RemindAt = Core.Time.LocalTime.ToLocalDateTime(DateTime.Now.AddHours(-1))
        }).Data!;
        _service.RemindersSetStatus(fired.Id, ReminderStatus.Fired);

        _service.RemindersCreate(new CreateReminderInput
        {
            Title = "以后再说",
            RemindAt = Core.Time.LocalTime.ToLocalDateTime(DateTime.Now.AddDays(1))
        });

        var vm = new RemindersViewModel(_service, (_, _) => { });
        vm.Reload();

        var view = new RemindersView { DataContext = vm };
        view.Measure(new Size(1000, 700));
        view.Arrange(new Rect(0, 0, 1000, 700));

        Assert.NotNull(view.Content);
        Assert.True(view.Bounds.Width > 0);

        // 已到点的和未到点的合并成「待办」一组，没有第三条分组
        Assert.Equal(2, vm.Active.Count);
        Assert.Empty(vm.Handled);
        Assert.Equal("该处理了", vm.Active[0].Title);
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

    /// <summary>
    /// SukiSideMenu(TreeView) 会先处理 PointerPressed 并置 Handled，冒泡监听会漏掉部分点击。
    /// 用真实鼠标事件覆盖「点菜单项 → 切页」，保证导航改到隧道阶段后仍然有效。
    /// </summary>
    [AvaloniaFact]
    public void SideMenu_MouseClick_Navigates()
    {
        var vm = new MainWindowViewModel(_service);
        var window = new MainWindow(vm) { Width = 1280, Height = 820 };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();

        var item = window.GetVisualDescendants().OfType<SukiSideMenuItem>()
            .Single(i => i.Tag as string == "Projects");
        var center = item.TranslatePoint(new Point(item.Bounds.Width / 2, item.Bounds.Height / 2), window);
        Assert.NotNull(center);

        window.MouseDown(center!.Value, MouseButton.Left, RawInputModifiers.None);
        window.MouseUp(center.Value, MouseButton.Left, RawInputModifiers.None);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(AppPage.Projects, vm.Page);
        window.Close();
    }

    [AvaloniaFact]
    public void MainWindow_ShowsDashboardPage_ThenNavigates()
    {
        var vm = new MainWindowViewModel(_service);
        var window = new MainWindow(vm);

        window.Show();
        Dispatcher.UIThread.RunJobs();

        Assert.True(window.IsVisible);
        Assert.IsType<DashboardViewModel>(vm.CurrentPage);
        Assert.Equal("仪表盘", vm.PageTitle);

        vm.ShowTasksCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();

        Assert.IsType<TasksViewModel>(vm.CurrentPage);
        Assert.Equal("任务", vm.PageTitle);

        vm.ShowProjectsCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();

        Assert.IsType<ProjectsViewModel>(vm.CurrentPage);
        Assert.Equal("项目", vm.PageTitle);

        vm.ShowRemindersCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();

        Assert.IsType<RemindersViewModel>(vm.CurrentPage);
        Assert.Equal("提醒", vm.PageTitle);

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

        // 切换筛选会原地同步选项列表；早先的 Clear() + 重建会让每个项目出现两次，
        // 所以这里继续守着「选项既不重复、顺序也稳定」。
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

    /// <summary>
    /// 任务页渲染出来的顺序必须真的是按优先级排的 ——
    /// 视图模型排对了但模板里顺序被改回去，这条就守不住。
    /// </summary>
    [AvaloniaFact]
    public void TasksPage_RendersTasksInPriorityOrder()
    {
        var due = Core.Time.LocalTime.ToLocalDateTime(DateTime.Today.AddHours(18));
        _service.TasksCreate(new CreateTaskInput { Title = "低优先", Priority = Priority.Low, DueAt = due });
        _service.TasksCreate(new CreateTaskInput { Title = "高优先", Priority = Priority.High, DueAt = due });
        _service.TasksCreate(new CreateTaskInput { Title = "中优先", Priority = Priority.Medium, DueAt = due });

        var vm = new MainWindowViewModel(_service);
        var window = new MainWindow(vm);
        window.Show();
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();

        vm.ShowTasksCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();

        // 按视觉树里出现的先后取标题，不依赖 ItemsControl 的内部结构
        var rendered = window.GetVisualDescendants()
            .OfType<TextBlock>()
            .Select(t => t.Text)
            .Where(t => t is "高优先" or "中优先" or "低优先")
            .ToList();

        Assert.Equal(["高优先", "中优先", "低优先"], rendered);

        window.Close();
    }

    /// <summary>
    /// 设置入口在标题栏（最小化按钮旁），左边还有快速切换明暗的按钮；
    /// 两颗按钮和窗口按钮共用 SukiUI 的 WindowControlsButton 外观（同尺寸、同悬停底），
    /// 图标也是同一档细线条。
    ///
    /// 版本号不再挂在侧边栏页脚（菜单底部要保持干净），挪到了设置页的「关于」。
    /// </summary>
    [AvaloniaFact]
    public void Settings_SitsInTitlebar_WithThemeToggle_AndAboutShowsAuthor()
    {
        var vm = new MainWindowViewModel(_service);
        var window = new MainWindow(vm);
        window.Show();
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();

        // 侧边栏不再显示版本号：菜单底部保持干净，版本只在设置页「关于」里
        var sidebarVersion = window.GetVisualDescendants()
            .OfType<SukiSideMenuItem>()
            .SelectMany(i => i.GetVisualDescendants().OfType<TextBlock>())
            .Any(t => t.Text is not null && t.Text.StartsWith("V ", StringComparison.Ordinal));
        Assert.False(sidebarVersion, "侧边栏里还有版本号");

        var navLabels = window.GetVisualDescendants()
            .OfType<Button>()
            .Where(b => b.Classes.Contains("nav"))
            .SelectMany(b => b.GetVisualDescendants().OfType<TextBlock>())
            .Select(t => t.Text)
            .ToList();

        Assert.DoesNotContain("设置", navLabels);

        // 标题栏：两颗自定义按钮 + 最小化/最大化/关闭，都该是同一套外观
        var titleButtons = window.GetVisualDescendants()
            .OfType<Button>()
            .Where(b => b.Classes.Contains("WindowControlsButton"))
            .ToList();
        Assert.True(titleButtons.Count >= 5, $"标题栏里的窗口样式按钮不足：{titleButtons.Count}");

        // 五个图标必须**等距**。SukiUI 模板只给窗口按钮那一层 StackPanel 设了 Spacing=7，
        // 装自定义按钮的 ItemsControl 没有 Spacing：两颗自定义按钮就贴在一起（中心距 25），
        // 再和最小化之间空出 7px（中心距 32），一排图标节奏就断了。
        var titleCenters = titleButtons
            .Where(b => b.IsVisible && b.Bounds.Width > 0)
            .Select(b => b.TranslatePoint(new Point(b.Bounds.Width / 2, 0), window)!.Value.X)
            .OrderBy(x => x)
            .ToList();
        Assert.Equal(5, titleCenters.Count);

        var centerDeltas = titleCenters.Zip(titleCenters.Skip(1), (a, b) => b - a).ToList();
        for (var i = 1; i < centerDeltas.Count; i++)
        {
            Assert.True(Math.Abs(centerDeltas[i] - centerDeltas[0]) < 1.0,
                $"标题栏图标间距不一致：{string.Join(" / ", centerDeltas.Select(d => d.ToString("0.#")))}");
        }

        var settingsButton = titleButtons.First(b => (ToolTip.GetTip(b) as string) == "设置");
        settingsButton.Command!.Execute(null);
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(AppPage.Settings, vm.Page);

        // 关于：版本 + 作者
        var settingsTexts = window.GetVisualDescendants()
            .OfType<TextBlock>()
            .Select(t => t.Text)
            .ToList();
        Assert.Contains(vm.Settings!.Version, settingsTexts);
        Assert.Contains("Joont", settingsTexts);

        // 快速切换明暗：一点变深色，再点回浅色
        var themeButton = titleButtons.First(b => (ToolTip.GetTip(b) as string) == "切换深色/浅色");

        vm.Settings!.SetThemeCommand.Execute("light");
        themeButton.Command!.Execute(null);
        Assert.Equal("dark", vm.ThemeMode);
        themeButton.Command!.Execute(null);
        Assert.Equal("light", vm.ThemeMode);

        window.Close();
    }

    /// <summary>
    /// 仪表盘：三张时间卡（今日/本周/本月）+ 趋势 + 两个分布都渲染出来；
    /// 任务清单**常驻**（默认今日），点别的卡只换内容、不会把它收起来。
    ///
    /// 清单常驻的原因：收起时左栏只剩一条提示、右栏还是整块项目卡，
    /// 页面上直接空出一大块。这里连「两栏底边对齐」一起守住。
    /// </summary>
    [AvaloniaFact]
    public void Dashboard_RendersPeriodCards_WithAlwaysVisibleTaskList()
    {
        _service.TasksCreate(new CreateTaskInput
        {
            Title = "今天要做的事",
            // 用当天最后一分钟，避开「现在 + 18 点」跨到明天的情况
            DueAt = Core.Time.LocalTime.ToLocalDateTime(DateTime.Today.AddHours(23).AddMinutes(59))
        });
        _service.TasksCreate(new CreateTaskInput
        {
            Title = "已经逾期的",
            // 以当天零点为基准加天数，恒为「昨天 10:00」；
            // 若用 DateTime.Now.AddDays(-1)，每天 14:00 之后 +10h 会跨过午夜落回今天
            DueAt = Core.Time.LocalTime.ToLocalDateTime(DateTime.Today.AddDays(-1).AddHours(10))
        });
        // 右栏要有东西才能量「两栏同高」；顺便覆盖「有项目」的正常情况
        _service.ProjectsCreate(new CreateProjectInput { Name = "验收项目" });

        var vm = new MainWindowViewModel(_service);
        var window = new MainWindow(vm);
        window.Show();
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();

        vm.ShowDashboardCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(AppPage.Dashboard, vm.Page);
        Assert.NotNull(vm.Dashboard);

        var labels = window.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text).ToList();
        Assert.Contains("今日", labels);
        Assert.Contains("本周", labels);
        Assert.Contains("本月", labels);

        // 今日卡上的待完成数：今天到期的 1 个（逾期的那个按看板口径归逾期）
        var today = vm.Dashboard.Periods.Single(p => p.Range == DashboardRange.Today);
        Assert.Equal(1, today.Due);

        // 趋势 7 天 + 状态 4 项 + 优先级 4 项
        Assert.Equal(7, vm.Dashboard.Trend.Count);
        Assert.Equal(4, vm.Dashboard.StatusBreakdown.Count);
        Assert.Equal(4, vm.Dashboard.PriorityBreakdown.Count);

        // 清单常驻：一张卡被选中，清单直接跟在卡片下面，不需要先点一下
        Assert.True(today.IsSelected);
        Assert.Same(today, vm.Dashboard.SelectedPeriod);
        Assert.Equal(today.Due, today.Tasks.Count);
        Assert.True(
            window.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text == "今天要做的事"),
            "清单里应出现那条任务");

        // 两栏底边对齐：清单卡和项目卡同高，右栏不会短一截空出一块
        Border? CardByText(string text) => window.GetVisualDescendants().OfType<TextBlock>()
            .Where(t => t.Text == text)
            .SelectMany(t => t.GetVisualAncestors().OfType<Border>())
            .FirstOrDefault(b => b.Classes.Contains("card"));

        var listCard = CardByText("今日 的任务清单");
        var projectCard = CardByText("项目进度");
        Assert.NotNull(listCard);
        Assert.NotNull(projectCard);

        var listBottom = listCard!.TranslatePoint(new Point(0, listCard.Bounds.Height), window);
        var projectBottom = projectCard!.TranslatePoint(new Point(0, projectCard.Bounds.Height), window);
        Assert.NotNull(listBottom);
        Assert.NotNull(projectBottom);
        Assert.True(
            Math.Abs(listBottom!.Value.Y - projectBottom!.Value.Y) < 2,
            $"清单卡与项目卡底部没对齐：{listBottom.Value.Y} / {projectBottom.Value.Y}");

        // 点「本周」：清单内容换成本周，而且再点一次也不会收起来
        var week = vm.Dashboard.Periods.Single(p => p.Range == DashboardRange.Week);
        vm.Dashboard.SelectPeriodCommand.Execute(week);
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();

        Assert.True(
            window.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text == "本周 的任务清单"),
            "切换后清单标题应变成「本周 的任务清单」");

        vm.Dashboard.SelectPeriodCommand.Execute(week);
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();

        Assert.True(week.IsSelected);
        Assert.True(
            window.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text == "本周 的任务清单"),
            "再点一次当前卡片不应把清单收起来");

        window.Close();
    }

    /// <summary>
    /// 清单为空时给一句话提示 —— 固定高度的卡片里空着一大片，看着像没加载出来。
    /// </summary>
    [AvaloniaFact]
    public void Dashboard_EmptyTaskList_ShowsHint()
    {
        var vm = new MainWindowViewModel(_service);
        var window = new MainWindow(vm) { Width = 1280, Height = 820 };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();

        vm.ShowDashboardCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();

        Assert.Empty(vm.Dashboard!.SelectedPeriod.Tasks);
        Assert.True(
            window.GetVisualDescendants().OfType<TextBlock>()
                .Any(t => t.Text == "这段时间没有到期的任务"),
            "空清单应显示一句说明");

        window.Close();
    }

    /// <summary>
    /// 仪表盘最下方的左侧图表要拉到与右列一样高（底部对齐），
    /// 而且柱子按可用高度缩放 —— 卡片拉高了、柱子还那么点，就等于没拉。
    /// </summary>
    [AvaloniaFact]
    public void Dashboard_TrendChart_FillsTheBottomRow()
    {
        _service.TasksCreate(new CreateTaskInput
        {
            Title = "今天到期",
            DueAt = Core.Time.LocalTime.ToLocalDateTime(DateTime.Today.AddHours(23).AddMinutes(59))
        });

        var vm = new MainWindowViewModel(_service);
        var window = new MainWindow(vm) { Width = 1280, Height = 820 };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        vm.ShowDashboardCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();

        var chart = window.GetVisualDescendants().OfType<AppTrendChart>().FirstOrDefault();
        Assert.NotNull(chart);
        Assert.NotNull(chart!.Items);
        Assert.Equal(7, chart.Items!.Count);

        // 底部统计区现在是并排三张等高卡：趋势 / 状态分布 / 优先级分布。
        // 它们在同一个 Grid 行里，底边必须落在同一条线上。
        var card = chart.GetVisualAncestors().OfType<Border>().First(b => b.Classes.Contains("card"));
        var row = card.GetVisualAncestors().OfType<Grid>().First();
        var siblings = row.Children.OfType<Border>()
            .Where(b => b.Classes.Contains("card"))
            .ToList();
        Assert.True(siblings.Count >= 2, $"底部统计区应至少并排两张卡，实际 {siblings.Count}");

        var cardBottom = card.TranslatePoint(new Point(0, card.Bounds.Height), window);
        Assert.NotNull(cardBottom);
        foreach (var sibling in siblings)
        {
            var siblingBottom = sibling.TranslatePoint(new Point(0, sibling.Bounds.Height), window);
            Assert.NotNull(siblingBottom);
            Assert.True(
                Math.Abs(cardBottom!.Value.Y - siblingBottom!.Value.Y) < 2,
                $"底部卡片没有等高：card={cardBottom.Value.Y} sibling={siblingBottom.Value.Y}");
        }

        // 图表区被拉满了（旧的固定高度是 92）
        Assert.True(chart.Bounds.Height > 120, $"图表高度没有跟着卡片拉长：{chart.Bounds.Height}");

        // 柱子按可用高度画出来，不是 0
        var bars = chart.GetVisualDescendants().OfType<Border>()
            .Where(b => b.Bounds.Height > 1)
            .ToList();
        Assert.NotEmpty(bars);

        window.Close();
    }

    /// <summary>
    /// 任务弹框里开始与截止各有一套「日期 + 时刻」控件：
    /// 任务的起止默认继承项目，所以两头都得能填。
    /// </summary>
    [AvaloniaFact]
    public void TaskDialog_HasStartAndDuePickers()
    {
        var vm = new MainWindowViewModel(_service);
        var window = new MainWindow(vm) { Width = 1280, Height = 820 };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        vm.NewTaskCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs(DispatcherPriority.Loaded);
        Dispatcher.UIThread.RunJobs(DispatcherPriority.Background);
        window.UpdateLayout();

        Assert.Equal(2, window.GetVisualDescendants().OfType<CalendarDatePicker>().Count());
        Assert.Equal(2, window.GetVisualDescendants().OfType<TimePicker>().Count());

        window.Close();
    }

    /// <summary>项目弹框里起止日期是必填项，标签上写清楚。</summary>
    [AvaloniaFact]
    public void ProjectDialog_MarksDateRangeAsRequired()
    {
        var vm = new MainWindowViewModel(_service);
        var window = new MainWindow(vm) { Width = 1280, Height = 820 };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        vm.NewProjectCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs(DispatcherPriority.Loaded);
        Dispatcher.UIThread.RunJobs(DispatcherPriority.Background);
        window.UpdateLayout();

        var labels = window.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text).ToList();
        Assert.Contains("开始日期 *", labels);
        Assert.Contains("结束日期 *", labels);

        // 新建时先给一段默认区间
        Assert.NotNull(vm.Projects!.Form!.StartDate);
        Assert.NotNull(vm.Projects.Form.DueDate);

        window.Close();
    }

    /// <summary>
    /// 全站卡片必须用**同一套**边框。
    ///
    /// 症状：仪表盘的块、任务页的每条任务、项目卡片、提醒页的行各写各的，
    /// 圆角 6/8/10/12/20 混用，玻璃拟态与实心卡片混着来 —— 同一个 App 像几张不同的界面。
    ///
    /// 现在统一到 App.axaml 的全局样式（Classes="card" / "card-row"），
    /// 这里守住：跨页面取到的卡片，圆角、描边粗细、底色完全一致。
    /// </summary>
    [AvaloniaFact]
    public void Cards_ShareOneBorderStyleAcrossPages()
    {
        _service.TasksCreate(new CreateTaskInput { Title = "任务行" });
        _service.ProjectsCreate(new CreateProjectInput { Name = "项目" }).Data!.Id.ToString();
        _service.RemindersCreate(new CreateReminderInput
        {
            Title = "提醒行",
            RemindAt = Core.Time.LocalTime.ToLocalDateTime(DateTime.Now.AddHours(2))
        });

        var vm = new MainWindowViewModel(_service);
        var window = new MainWindow(vm) { Width = 1280, Height = 820 };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();

        // 收集各页面的卡片。Border 与 GlassCard 不是同一个类型，
        // 但「圆角 / 描边 / 底色」这三样是相通的 —— 把它们抽成同一组比较。
        //
        // 分两档收：块级卡片（仪表盘的块、项目卡片）与列表行（任务行、提醒行）。
        // 档位之间本来就该有差别（容器包内容，圆角自然更大），
        // 要守的是**同一档之内全站一致**。
        var blockRadii = new List<CornerRadius>();
        var rowRadii = new List<CornerRadius>();
        var blockThicknesses = new List<Thickness>();
        var blockBackgrounds = new List<string?>();
        var rowThicknesses = new List<Thickness>();
        var rowBackgrounds = new List<string?>();
        var count = 0;

        void CollectBlock(CornerRadius radius, Thickness thickness, string? background)
        {
            blockRadii.Add(radius);
            blockThicknesses.Add(thickness);
            blockBackgrounds.Add(background);
            count++;
        }

        void CollectRow(CornerRadius radius, Thickness thickness, string? background)
        {
            rowRadii.Add(radius);
            rowThicknesses.Add(thickness);
            rowBackgrounds.Add(background);
            count++;
        }

        vm.ShowDashboardCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        foreach (var b in window.GetVisualDescendants().OfType<Avalonia.Controls.Border>()
                     .Where(b => b.Classes.Contains("card"))
                     // 两种刻意的状态变体不参与一致性比较：
                     // .attention 是逾期警示底；.period.selected 是「当前清单属于哪段时间」的选中态
                     .Where(b => !b.Classes.Contains("attention"))
                     .Where(b => !b.Classes.Contains("selected")))
        {
            CollectBlock(b.CornerRadius, b.BorderThickness, b.Background?.ToString());
        }

        vm.ShowProjectsCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        foreach (var c in window.GetVisualDescendants().OfType<GlassCard>()
                     .Where(c => c.Classes.Contains("card")))
        {
            CollectBlock(c.CornerRadius, c.BorderThickness, c.Background?.ToString());
        }

        vm.ShowTasksCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        foreach (var b in window.GetVisualDescendants().OfType<Avalonia.Controls.Border>()
                     .Where(b => b.Classes.Contains("card-row")))
        {
            CollectRow(b.CornerRadius, b.BorderThickness, b.Background?.ToString());
        }

        vm.ShowRemindersCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        foreach (var b in window.GetVisualDescendants().OfType<Avalonia.Controls.Border>()
                     .Where(b => b.Classes.Contains("card-row")))
        {
            CollectRow(b.CornerRadius, b.BorderThickness, b.Background?.ToString());
        }

        Assert.True(count >= 4, $"应至少取到 4 张卡片，实际 {count}");
        Assert.NotEmpty(blockRadii);
        Assert.NotEmpty(rowRadii);

        // 块级卡片：四边等宽描边、同一档圆角、同一档半透明底色
        Assert.Single(blockThicknesses.Distinct());
        Assert.Single(blockBackgrounds.Distinct());
        Assert.Single(blockRadii.Distinct());

        // 列表行：没有四边框（只有底部一条发丝线）、直角、透明底
        Assert.Single(rowThicknesses.Distinct());
        Assert.Single(rowBackgrounds.Distinct());
        Assert.Single(rowRadii.Distinct());
        Assert.Equal(new Thickness(0, 0, 0, 1), rowThicknesses[0]);

        // 两档之间要有差别：行比块小一档，才看得出「容器包内容」
        Assert.True(
            rowRadii[0].TopLeft < blockRadii[0].TopLeft,
            "列表行的圆角应小于块级卡片");

        window.Close();
    }

    /// <summary>
    /// 项目进度环用 SukiUI 原生的 <c>CircleProgressBar</c>，不再自绘。
    ///
    /// 自绘版（AppCircleProgress）虽然轨道更明显，但线宽、端点、起始角都和
    /// SukiUI 自己的控件不一致，和旁边的进度条、按钮放一起像两个体系的零件。
    /// 这里守两件事：页面上确实是原生控件；模板真的套上了（轨道 + 进度弧两层，
    /// 线宽跟着 StrokeWidth 走），而不是一团空白。
    /// </summary>
    [AvaloniaFact]
    public void ProjectProgress_UsesSukiUisNativeCircleBar()
    {
        var vm = new MainWindowViewModel(_service);
        var window = new MainWindow(vm) { Width = 1280, Height = 820 };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();

        vm.ShowDashboardCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();

        var rings = window.GetVisualDescendants().OfType<CircleProgressBar>().ToList();
        Assert.NotEmpty(rings);

        foreach (var ring in rings)
        {
            // 尺寸被写死了（44 / 56 …）：圆必须是正圆且真的占到位置
            Assert.True(ring.Bounds.Width > 30, $"进度环没有分到宽度：{ring.Bounds}");
            Assert.True(
                Math.Abs(ring.Bounds.Width - ring.Bounds.Height) < 0.5,
                $"进度环不是正圆：{ring.Bounds}");

            // 模板里是「轨道 Arc + PART_ArcFill」两层；线宽由 StrokeWidth 模板绑定
            var arcs = ring.GetVisualDescendants().OfType<Avalonia.Controls.Shapes.Arc>().ToList();
            Assert.True(arcs.Count >= 2, "进度环模板没套上（找不到轨道与进度弧）");
            Assert.All(arcs, a => Assert.Equal(ring.StrokeWidth, a.StrokeThickness, 1));

            Assert.InRange(ring.Value, 0d, 100d);
        }

        window.Close();
    }

    /// <summary>
    /// 任务页的每条任务与提醒页的每条提醒共用同一套行外观（card-row）。
    /// 两边各写一份样式定义是之前不统一的根源，现在统一到全局样式。
    /// </summary>
    [AvaloniaFact]
    public void TaskRowsAndReminderRows_ShareSameStyle()
    {
        _service.TasksCreate(new CreateTaskInput { Title = "任务行" });
        _service.RemindersCreate(new CreateReminderInput
        {
            Title = "提醒行",
            RemindAt = Core.Time.LocalTime.ToLocalDateTime(DateTime.Now.AddHours(2))
        });

        var vm = new MainWindowViewModel(_service);
        var window = new MainWindow(vm) { Width = 1280, Height = 820 };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();

        // 必须在各自页面上**当场**取值：切走页面后控件脱离样式宿主，
        // 样式值会被清空，隔页再读拿到的是卸载后的旧对象，测出来的东西没意义。
        vm.ShowTasksCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        var taskRow = window.GetVisualDescendants().OfType<Avalonia.Controls.Border>()
            .First(b => b.Classes.Contains("card-row"));
        var taskStyle = (
            taskRow.CornerRadius,
            taskRow.BorderThickness,
            taskRow.Padding,
            taskRow.Background?.ToString());

        vm.ShowRemindersCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        var reminderRow = window.GetVisualDescendants().OfType<Avalonia.Controls.Border>()
            .First(b => b.Classes.Contains("card-row"));
        var reminderStyle = (
            reminderRow.CornerRadius,
            reminderRow.BorderThickness,
            reminderRow.Padding,
            reminderRow.Background?.ToString());

        Assert.Equal(taskStyle, reminderStyle);

        window.Close();
    }

    /// <summary>
    /// 仪表盘不能只有图表 —— 项目进度与近期任务这两块实体数据必须真的画出来。
    ///
    /// 症状：页面上全是图表，看不到具体是哪个项目、哪条任务。
    /// 这里守两件事：项目区块与近期任务区块的标题都在；
    /// 项目名、任务名、项目归属都渲染成了文字（不只是数字）。
    /// </summary>
    [AvaloniaFact]
    public void Dashboard_RendersProjectAndTaskSections()
    {
        var project = _service.ProjectsCreate(new CreateProjectInput { Name = "官网改版" }).Data!;
        _service.TasksCreate(new CreateTaskInput
        {
            Title = "重做首页",
            ProjectId = project.Id,
            DueAt = Core.Time.LocalTime.ToLocalDateTime(DateTime.Today.AddHours(23).AddMinutes(59))
        });
        _service.TasksCreate(new CreateTaskInput
        {
            Title = "独立任务没项目",
            DueAt = Core.Time.LocalTime.ToLocalDateTime(DateTime.Today.AddDays(-1).AddHours(10))
        });

        var vm = new MainWindowViewModel(_service);
        var window = new MainWindow(vm);
        window.Show();
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();

        vm.ShowDashboardCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();

        var labels = window.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text).ToList();

        // 两个区块的标题
        Assert.Contains("项目进度", labels);
        Assert.Contains("近期任务", labels);

        // 项目名 + 任务计数（该项目下只有「重做首页」这一条）
        Assert.Contains("官网改版", labels);
        Assert.Contains("1 个任务 · 进行中 0", labels);

        // 任务名（两条都在清单里）
        Assert.Contains("重做首页", labels);
        Assert.Contains("独立任务没项目", labels);

        // 数据模型也填上了
        Assert.Single(vm.Dashboard!.Projects);
        Assert.Equal(2, vm.Dashboard.UpcomingTasks.Count);

        window.Close();
    }

    /// <summary>
    /// 仪表盘上的两行实体数据必须真的点得动。
    ///
    /// 这两行是 Border + PointerPressed（不是 Button）—— SukiUI 的 Button 模板
    /// 把内容塞进横向 StackPanel，Grid 撑不满宽。这里用真实鼠标事件走一遍，
    /// 守住「点项目弹任务清单」「点任务开编辑弹窗」两条链路。
    /// </summary>
    [AvaloniaFact]
    public void Dashboard_RowsAreClickable()
    {
        var project = _service.ProjectsCreate(new CreateProjectInput { Name = "官网改版" }).Data!;
        _service.TasksCreate(new CreateTaskInput
        {
            Title = "重做首页",
            ProjectId = project.Id,
            DueAt = Core.Time.LocalTime.ToLocalDateTime(DateTime.Today.AddHours(23).AddMinutes(59))
        });

        var vm = new MainWindowViewModel(_service);
        // 窗口拉高：一屏装下整页仪表盘，最后的「近期任务」不用滚动就能点到
        var window = new MainWindow(vm) { Width = 1280, Height = 1400 };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();

        vm.ShowDashboardCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();

        // 点项目行 → 弹该项目的任务清单
        // 用 DataContext 定位（项目名文本在「近期任务」里也会出现一次，不能只按文字找）
        var projectRow = window.GetVisualDescendants()
            .OfType<Border>()
            .First(b => b.DataContext is DashboardProjectViewModel p && p.Name == "官网改版");
        var projectPoint = projectRow.TranslatePoint(
            new Point(projectRow.Bounds.Width / 2, projectRow.Bounds.Height / 2), window);
        Assert.NotNull(projectPoint);

        window.MouseDown(projectPoint!.Value, MouseButton.Left, RawInputModifiers.None);
        window.MouseUp(projectPoint.Value, MouseButton.Left, RawInputModifiers.None);
        Dispatcher.UIThread.RunJobs();

        Assert.NotNull(vm.ProjectTasksDialog);
        Assert.Equal(project.Id, vm.ProjectTasksDialog!.ProjectId);

        // 关掉弹窗，再点任务行 → 打开编辑弹窗
        vm.CloseProjectTasksDialog();
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();

        // 「近期任务」现在排在仪表盘最下面：窗口拉高到能一屏装下整页，
        // 就不用靠滚动去够最后那几行（滚动后的命中坐标在无头环境里算不准）
        var taskRow = window.GetVisualDescendants()
            .OfType<Border>()
            .First(b => b.DataContext is DashboardTaskViewModel t && t.Title == "重做首页");
        var taskPoint = taskRow.TranslatePoint(
            new Point(taskRow.Bounds.Width / 2, taskRow.Bounds.Height / 2), window);
        Assert.NotNull(taskPoint);

        window.MouseDown(taskPoint!.Value, MouseButton.Left, RawInputModifiers.None);
        window.MouseUp(taskPoint.Value, MouseButton.Left, RawInputModifiers.None);
        Dispatcher.UIThread.RunJobs();

        Assert.True(vm.Tasks!.IsFormOpen, "点任务行应打开任务编辑弹窗");

        window.Close();
    }

    /// <summary>
    /// 在下拉里选过项目之后，折叠框里必须仍然显示这个项目。
    ///
    /// 症状：选中项目后框里空白。成因是刷新时把选项集合 Clear() 再逐项 Add()：
    /// ComboBox 会先把 SelectedItem 置空，而补回来的又是「值相等的新实例」，
    /// 属性变更被相等判断吃掉，SelectionBoxItem 就再也没有被重新赋值。
    /// 现在选项原地同步、实例不变，这里守住三件事：选中项实例仍在列表里、
    /// SelectionBoxItem 非空、折叠框真的画出了项目名。
    /// </summary>
    [AvaloniaFact]
    public void ProjectDropdown_KeepsSelectedText_AfterUserPicks()
    {
        _service.ProjectsCreate(new CreateProjectInput { Name = "P1" });
        _service.ProjectsCreate(new CreateProjectInput { Name = "P2" });

        var vm = new MainWindowViewModel(_service);
        vm.ShowBoardCommand.Execute(null);   // 启动页是仪表盘，先到看板用它的项目下拉
        var window = new MainWindow(vm);
        window.Show();
        Dispatcher.UIThread.RunJobs();

        // 看板：全部项目 / P1 / P2 → 第 2 项是 P2
        AssertDropdownShows(window, vm.Board!.ProjectOptions, () => vm.Board.SelectedProject, 2, "P2");

        vm.ShowTasksCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();

        // 任务页：同样是全部项目 / P1 / P2，换一项验证
        AssertDropdownShows(window, vm.Tasks!.ProjectOptions, () => vm.Tasks.SelectedProject, 1, "P1");

        window.Close();
    }

    private static void AssertDropdownShows(
        Window window,
        IReadOnlyList<ProjectOption> options,
        Func<ProjectOption?> selected,
        int index,
        string expectedName)
    {
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();

        var combo = window.GetVisualDescendants()
            .OfType<ComboBox>()
            .First(c => ReferenceEquals(c.ItemsSource, options));

        // 等价于用户展开下拉、点中第 index 项
        combo.IsDropDownOpen = true;
        Dispatcher.UIThread.RunJobs();
        combo.SelectedIndex = index;
        Dispatcher.UIThread.RunJobs();
        combo.IsDropDownOpen = false;
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();

        Assert.Same(combo.SelectedItem, selected());
        Assert.NotNull(combo.SelectionBoxItem);
        Assert.Contains(expectedName, combo.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text));
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

        // 数字键切页（顺序同左侧菜单：仪表盘 → 看板 → 任务 → 项目 → 提醒 → 设置）
        Press(window, Key.D1);
        Dispatcher.UIThread.RunJobs();
        Assert.IsType<DashboardViewModel>(vm.CurrentPage);

        Press(window, Key.D2);
        Dispatcher.UIThread.RunJobs();
        Assert.IsType<BoardViewModel>(vm.CurrentPage);

        Press(window, Key.D3);
        Dispatcher.UIThread.RunJobs();
        Assert.IsType<TasksViewModel>(vm.CurrentPage);

        Press(window, Key.D4);
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
                // 图标一律用 SukiUI 主题下的 PathIcon（不再有自绘 Icon 控件）
                if (button.Content is not PathIcon && !button.GetVisualDescendants().OfType<PathIcon>().Any())
                {
                    problems.Add($"{page}: 图标按钮里没有 PathIcon");
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

        // 提醒页的图标按钮只存在于行上，先造一条待处理 + 一条待触发
        var fired = _service.RemindersCreate(new CreateReminderInput
        {
            Title = "巡检提醒",
            RemindAt = Core.Time.LocalTime.ToLocalDateTime(DateTime.Now.AddHours(-1))
        }).Data!;
        _service.RemindersSetStatus(fired.Id, ReminderStatus.Fired);
        _service.RemindersCreate(new CreateReminderInput
        {
            Title = "巡检待触发",
            RemindAt = Core.Time.LocalTime.ToLocalDateTime(DateTime.Now.AddDays(1))
        });

        vm.ShowRemindersCommand.Execute(null);
        Inspect("提醒");

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

        var composer = window.GetVisualDescendants().OfType<GlassCard>().First(b => b.Classes.Contains("composer"));
        var box = composer.GetVisualDescendants().OfType<TextBox>().First();
        var presenter = box.GetVisualDescendants().OfType<TextPresenter>().First();
        // SukiUI 的 Button 模板里有多个 ContentPresenter，前两个是 0×0 的占位层，
        // 图标尺寸变化后占位层的尺寸也会变。直接量真正画出来的 PathIcon 更稳。
        var icon = composer.GetVisualDescendants().OfType<PathIcon>().First();

        double Center(Control control) =>
            control.TranslatePoint(default, composer)!.Value.Y + control.Bounds.Height / 2;

        var delta = Math.Abs(Center(presenter) - Center(icon));
        Assert.True(delta <= 1.0,
            $"文字与图标中心线相差 {delta:F1}px：text={presenter.Bounds} icon={icon.Bounds}");
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

    /// <summary>
    /// 看板列容器换成 Suki 的 GlassCard 之后，拖放回调里仍在判断 Border，
    /// 落下时直接 return —— 跨列拖拽完全失效。这条用例覆盖 DragOver + Drop 两步，
    /// 验证事件真正走到 ViewModel 并把任务状态改掉。
    /// </summary>
    /// <summary>
    /// Suki 的 TimePicker 模板把时/分文本写死成 Center，且 PART_FlyoutButton 的
    /// MinWidth 是 242 —— 弹框列宽不够时右侧日历图标会被裁掉。这里验证给足宽度、
    /// 文本靠左两件事都真的作用在渲染出来的控件上。
    /// </summary>
    [AvaloniaFact]
    public void TaskDialog_TimePicker_IsWideEnoughAndLeftAligned()
    {
        var vm = new MainWindowViewModel(_service);
        var window = new MainWindow(vm) { Width = 1280, Height = 820 };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        vm.NewTaskCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs(DispatcherPriority.Loaded);
        Dispatcher.UIThread.RunJobs(DispatcherPriority.Background);
        window.UpdateLayout();

        var picker = window.GetVisualDescendants().OfType<TimePicker>().FirstOrDefault();
        Assert.NotNull(picker);
        Assert.True(picker!.Width >= 242, $"时间控件宽度不足以容纳右侧图标：{picker.Width}");

        var texts = picker.GetVisualDescendants().OfType<TextBlock>().ToList();
        Assert.NotEmpty(texts);
        Assert.All(texts, t => Assert.Equal(HorizontalAlignment.Left, t.HorizontalAlignment));

        window.Close();
    }

    [AvaloniaFact]
    public void Board_DropOnAnotherColumn_MovesTaskStatus()
    {
        var task = _service.TasksCreate(new CreateTaskInput { Title = "拖我到别的进度" }).Data!;

        var vm = new MainWindowViewModel(_service);
        vm.ShowBoardCommand.Execute(null);   // 启动页是仪表盘，拖拽看板列要先切过去
        var window = new MainWindow(vm) { Width = 1280, Height = 820 };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();

        var board = window.GetVisualDescendants().OfType<BoardView>().Single();
        var columns = board.GetVisualDescendants().OfType<GlassCard>()
            .Where(c => c.Classes.Contains("column") && c.DataContext is BoardColumnViewModel)
            .ToList();

        Assert.NotEmpty(columns);

        var origin = columns.First(c => ((BoardColumnViewModel)c.DataContext!).Cards.Any(k => k.Task.Id == task.Id));
        var originStatus = ((BoardColumnViewModel)origin.DataContext!).Status;
        var target = columns.First(c => ((BoardColumnViewModel)c.DataContext!).Status != originStatus);
        var targetStatus = ((BoardColumnViewModel)target.DataContext!).Status;

        var transfer = new DataTransfer();
        transfer.Add(DataTransferItem.Create(
            DataFormat.CreateInProcessFormat<string>("workstation.board.task-id"), task.Id));

        var over = new DragEventArgs(DragDrop.DragOverEvent, transfer, target, default, KeyModifiers.None);
        target.RaiseEvent(over);
        Assert.Equal(DragDropEffects.Move, over.DragEffects);

        var drop = new DragEventArgs(DragDrop.DropEvent, transfer, target, default, KeyModifiers.None);
        target.RaiseEvent(drop);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(targetStatus, _service.TasksGet(task.Id).Data!.Status);

        window.Close();
    }

    [AvaloniaFact]
    public void Board_TaskCard_DoubleClick_OpensEditor()
    {
        var task = _service.TasksCreate(new CreateTaskInput { Title = "双击我" }).Data!;

        var vm = new MainWindowViewModel(_service);
        // 启动页是仪表盘，这个用例要看板上的卡片，先切到看板
        vm.ShowBoardCommand.Execute(null);
        var window = new MainWindow(vm);
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var card = window.GetVisualDescendants()
            .OfType<GlassCard>()
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

        // 双击卡片直接弹出编辑弹窗，不再跳转到任务页
        Assert.Equal(AppPage.Board, vm.Page);
        Assert.True(vm.Tasks!.IsFormOpen, "双击卡片应直接弹出编辑弹窗");

        window.Close();
    }

    [AvaloniaFact]
    public void PaletteNewProject_RendersProjectDialog()
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

        // 编辑弹框必须真的长出来：只断言 IsFormOpen 会漏掉「表单为空 / 高度塌成 0」
        var host = window.GetVisualDescendants().OfType<SukiDialogHost>().Single();
        var card = host.GetVisualDescendants().OfType<GlassCard>().FirstOrDefault();

        Assert.NotNull(card);
        Assert.True(card!.IsVisible, "弹框应可见");
        Assert.True(card.Bounds.Width > 300, $"弹框宽度异常：{card.Bounds}");
        Assert.True(card.Bounds.Height > 300, $"弹框高度异常：{card.Bounds}");
        Assert.True(host.GetVisualDescendants().OfType<TextBox>().Any(), "表单字段应渲染出来");

        window.Close();
    }

    [AvaloniaFact]
    public void PaletteNewProject_RowCommand_OpensDialog()
    {
        var vm = new MainWindowViewModel(_service);
        var window = new MainWindow(vm);
        window.Show();
        Dispatcher.UIThread.RunJobs();

        vm.OpenPaletteCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();

        var row = window.GetVisualDescendants()
            .OfType<Button>()
            .First(b => b.DataContext is PaletteItem { Title: "新建项目" });

        // SukiDialogHost 的入场动画在无头环境里不会落地，窗口级鼠标坐标会落在过渡态
        // 的位置上。这里执行按钮绑定的命令，仍然验证「面板项 → 打开弹框」这条链。
        row.Command!.Execute(null);
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(AppPage.Projects, vm.Page);
        Assert.True(vm.Projects!.IsFormOpen, "点「新建项目」应打开项目弹框");
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

    /// <summary>
    /// 项目任务弹窗的快速添加：输入区必须够大（多行、可写好几条），
    /// 而且 Ctrl+Enter 能直接提交 —— 以前是 58 高的小框 + 贴在角落里的小按钮。
    /// </summary>
    [AvaloniaFact]
    public void ProjectTasksDialog_QuickAdd_IsRoomyAndSubmitsWithCtrlEnter()
    {
        var project = _service.ProjectsCreate(new CreateProjectInput { Name = "验收项目" }).Data!;
        var dialogVm = new ProjectTasksDialogViewModel(project.Id, _service, (_, _) => { });
        var view = new ProjectTasksDialogView { DataContext = dialogVm };

        var window = new Window { Width = 820, Height = 560, Content = view };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();

        var box = view.GetVisualDescendants().OfType<TextBox>().Single();
        Assert.True(box.MinHeight >= 90, $"快速添加框太矮：{box.MinHeight}");
        Assert.True(box.AcceptsReturn, "快速添加框应支持多行");

        // 按钮与输入框同一行，且按钮有足够宽度（不是角落里的一个小方块）
        var addButton = view.GetVisualDescendants().OfType<Button>()
            .Single(b => b.Content as string == "添加");
        Assert.True(addButton.Bounds.Width >= 84, $"添加按钮太窄：{addButton.Bounds.Width}");
        Assert.True(addButton.Bounds.Height >= 36, $"添加按钮太矮：{addButton.Bounds.Height}");

        box.Focus();
        dialogVm.QuickAddText = "第一件事\n第二件事";
        Dispatcher.UIThread.RunJobs();

        window.KeyPress(Key.Enter, RawInputModifiers.Control, PhysicalKey.Enter, null);
        Dispatcher.UIThread.RunJobs();

        var titles = _service.TasksList().Data!
            .Where(t => t.ProjectId == project.Id)
            .Select(t => t.Title)
            .ToList();
        Assert.Equal(["第一件事", "第二件事"], titles);
        Assert.Equal(string.Empty, dialogVm.QuickAddText);

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

    [AvaloniaFact]
    public void ReportsView_LoadsXaml()
    {
        var vm = new ReportsViewModel(_service, (_, _) => { });
        vm.Reload();

        var view = new ReportsView { DataContext = vm };
        view.Measure(new Size(1000, 700));
        view.Arrange(new Rect(0, 0, 1000, 700));

        Assert.NotNull(view.Content);
    }
}
