using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Microsoft.Data.Sqlite;
using SukiUI.Controls;
using WorkStation.App.ViewModels;
using WorkStation.App.Views;
using WorkStation.Core.Dashboard;
using WorkStation.Core.Models;
using WorkStation.Core.Services;
using WorkStation.Core.Time;

namespace WorkStation.Tests;

/// <summary>
/// 截图验收用例：改动主题或控件样式后跑一次，到 .shots 里逐页看浅色/深色效果。
/// 只依赖内存库与 headless 渲染，不碰真实数据。
/// </summary>
public class VisualCheck
{
    private const string OutDir = @"E:\Joont\AI Agent Workspace\work-station\.shots";

    [AvaloniaFact]
    public void CaptureShell()
    {
        Directory.CreateDirectory(OutDir);

        using var connection = TestDb.OpenMigrated();
        var service = new WorkStationService(connection, ":memory:");

        var project = service.ProjectsCreate(new CreateProjectInput
        {
            Name = "官网改版",
            Status = ProjectStatus.Active,
            Color = "#2F9BFF",
            Priority = Priority.High,
            StartDate = "2026-11-01",
            DueDate = "2026-12-01"
        }).Data!;

        var second = service.ProjectsCreate(new CreateProjectInput
        {
            Name = "季度复盘",
            Status = ProjectStatus.Active,
            Color = "#7A5AF8"
        }).Data!;

        var today = DateTime.Today;

        service.TasksCreate(new CreateTaskInput
        {
            Title = "把首页文案定下来",
            ProjectId = project.Id,
            Status = TaskStatus.Doing,
            Priority = Priority.High,
            StartAt = LocalTime.ToLocalDateTime(today.AddDays(-1).AddHours(9)),
            DueAt = LocalTime.ToLocalDateTime(today.AddHours(18))
        });

        service.TasksCreate(new CreateTaskInput
        {
            Title = "导出上周访问数据",
            ProjectId = project.Id,
            Status = TaskStatus.Todo,
            DueAt = LocalTime.ToLocalDateTime(today.AddDays(-1).AddHours(10))
        });

        service.TasksCreate(new CreateTaskInput
        {
            Title = "跟设计对一版栅格",
            ProjectId = second.Id,
            Status = TaskStatus.Todo,
            Priority = Priority.Medium,
            DueAt = LocalTime.ToLocalDateTime(today.AddHours(21))
        });

        service.TasksCreate(new CreateTaskInput
        {
            Title = "整理与供应商的合同附件",
            ProjectId = second.Id,
            Status = TaskStatus.Blocked
        });

        // 同一天到期、只有优先级不同的三条：验收截图里能直接看出排序按优先级走
        // （截止时间越晚的反而排越前，说明主排序键确实是优先级）
        service.TasksCreate(new CreateTaskInput
        {
            Title = "低优先：整理旧素材",
            Status = TaskStatus.Todo,
            Priority = Priority.Low,
            DueAt = LocalTime.ToLocalDateTime(today.AddHours(12))
        });
        service.TasksCreate(new CreateTaskInput
        {
            Title = "高优先：上线前回归",
            Status = TaskStatus.Todo,
            Priority = Priority.High,
            DueAt = LocalTime.ToLocalDateTime(today.AddHours(16))
        });
        service.TasksCreate(new CreateTaskInput
        {
            Title = "中优先：同步进度给客户",
            Status = TaskStatus.Todo,
            Priority = Priority.Medium,
            DueAt = LocalTime.ToLocalDateTime(today.AddHours(20))
        });

        service.TasksCreate(new CreateTaskInput
        {
            Title = "把看板的空态画出来",
            Status = TaskStatus.Done
        });

        var reminder = service.RemindersCreate(new CreateReminderInput
        {
            Title = "给客户回邮件",
            RemindAt = LocalTime.ToLocalDateTime(DateTime.Now.AddHours(-1))
        }).Data!;
        service.RemindersSetStatus(reminder.Id, ReminderStatus.Fired);

        var vm = new MainWindowViewModel(service);
        var window = new MainWindow(vm) { Width = 1280, Height = 820 };

        window.Show();
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();

        // 启动页是仪表盘：先留一张「打开就是这个样子」的首屏，再切到看板
        Save(window, "s00-startup-dashboard.png");
        vm.ShowBoardCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        Save(window, "s01-board-light.png");

        // 项目看板的四列也做了状态配色，单独留一张验收图
        vm.Board?.SwitchBoardCommand.Execute("Projects");
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        Save(window, "s09-project-board.png");
        vm.Board?.SwitchBoardCommand.Execute("Tasks");

        // 窄窗体下的看板（诊断「缩小窗体后内容显示不完整」）：最小宽高各来一张
        window.Width = 1024;
        window.Height = 640;
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs(DispatcherPriority.Background);
        window.UpdateLayout();
        Save(window, "s12-board-narrow.png");
        vm.Board?.SwitchBoardCommand.Execute("Projects");
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs(DispatcherPriority.Background);
        Save(window, "s13-project-board-narrow.png");
        vm.Board?.SwitchBoardCommand.Execute("Tasks");
        window.Width = 1280;
        window.Height = 820;
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();

        // 项目页卡片：右上入口层次 + 操作区「查看任务」按钮
        vm.ShowProjectsCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        Save(window, "s10-projects.png");

        // 仪表盘：今日/本周/本月三张卡 + 7 天趋势 + 状态/优先级分布 + 项目进度 + 近期任务
        vm.ShowDashboardCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs(DispatcherPriority.Background);
        window.UpdateLayout();
        Save(window, "s14-dashboard.png");

        // 切到「本周」的清单：确认切换后卡片标题与内容跟着变，且清单常驻、两栏同高
        var weekPeriod = vm.Dashboard?.Periods.First(p => p.Range == DashboardRange.Week);
        if (weekPeriod is not null)
        {
            vm.Dashboard?.SelectPeriodCommand.Execute(weekPeriod);
        }
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs(DispatcherPriority.Background);
        window.UpdateLayout();
        Save(window, "s15-dashboard-expanded.png");

        // 仪表盘滚到底：底部两栏（趋势图 + 状态/优先级分布）也留一张，
        // 趋势图卡片必须和右列一样高
        var dashScroller = window.GetVisualDescendants().OfType<DashboardView>()
            .SelectMany(v => v.GetVisualDescendants().OfType<ScrollViewer>())
            .FirstOrDefault();
        if (dashScroller is not null)
        {
            dashScroller.Offset = new Vector(0, dashScroller.Extent.Height);
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
            Dispatcher.UIThread.RunJobs(DispatcherPriority.Background);
            window.UpdateLayout();
            Save(window, "s16-dashboard-bottom.png");
        }

        vm.ShowBoardCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();

        // 项目任务弹窗：左侧放大进度环 + 右侧垂直 stepper 任务清单
        var dialogProject = service.ProjectsCreate(new CreateProjectInput
        {
            Name = "弹窗验收项目",
            Status = ProjectStatus.Active,
            Color = "#2F9BFF"
        }).Data!;
        service.TasksCreate(new CreateTaskInput { Title = "已交付的官网首页", ProjectId = dialogProject.Id, Status = TaskStatus.Done });
        service.TasksCreate(new CreateTaskInput { Title = "正在重排版式", ProjectId = dialogProject.Id, Status = TaskStatus.Doing });
        service.TasksCreate(new CreateTaskInput
        {
            Title = "补齐验收用例",
            ProjectId = dialogProject.Id,
            Status = TaskStatus.Todo,
            DueAt = LocalTime.ToLocalDateTime(today.AddDays(-1).AddHours(10))
        });
        service.TasksCreate(new CreateTaskInput { Title = "整理交付清单", ProjectId = dialogProject.Id, Status = TaskStatus.Todo });

        // 项目任务弹窗：左侧放大进度环 + 右侧垂直 stepper 任务清单
        // 无头环境下 Suki 弹窗入场动画不会落地（opacity 卡在 0），直接截主窗口只会拍到背后的页面。
        // 这里单独起一个 SukiWindow 承载弹窗内容控件来截图；Suki 主题资源在 App 级可用，无需真实弹窗。
        var dialogVm = new ProjectTasksDialogViewModel(dialogProject.Id, service, (_, _) => { });
        var dialogView = new ProjectTasksDialogView { DataContext = dialogVm, Width = 820, Height = 560 };
        using var dlgWindow = new SukiWindow
        {
            Width = 820,
            Height = 604,   // 内容 560 + SukiWindow 自己那条标题栏
            Content = dialogView,
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
            Title = "项目任务清单"
        };
        dlgWindow.Show();
        Dispatcher.UIThread.RunJobs();
        dlgWindow.UpdateLayout();
        Dispatcher.UIThread.RunJobs(DispatcherPriority.Background);
        dlgWindow.UpdateLayout();
        Dispatcher.UIThread.RunJobs(DispatcherPriority.Background);

        var dlgFrame = dlgWindow.CaptureRenderedFrame();
        if (dlgFrame is not null)
            dlgFrame.Save(Path.Combine(OutDir, "s11-project-tasks-dialog.png"), PngBitmapEncoderOptions.Default);
        dlgWindow.Close();
        Dispatcher.UIThread.RunJobs();

        // 命令面板：行是自定义样式（左对齐、淡悬停、带描边的类型块），单独截一张
        var paletteVm = new CommandPaletteViewModel(service,
        [
            new PaletteAction("任务看板", "跳转", "看", ["board", "kanban"], () => { }),
            new PaletteAction("全部任务", "跳转", "任", ["tasks", "list"], () => { }),
            new PaletteAction("新建任务", "新建", "新", ["new", "task"], () => { })
        ]);
        paletteVm.Open();
        Dispatcher.UIThread.RunJobs();

        var paletteView = new CommandPaletteView { DataContext = paletteVm, Width = 580 };
        using var paletteWindow = new SukiWindow
        {
            Width = 580,
            Height = 460,
            Content = paletteView,
            Title = "命令面板"
        };
        paletteWindow.Show();
        Dispatcher.UIThread.RunJobs();
        paletteWindow.UpdateLayout();
        Dispatcher.UIThread.RunJobs(DispatcherPriority.Background);
        paletteWindow.UpdateLayout();

        var paletteFrame = paletteWindow.CaptureRenderedFrame();
        if (paletteFrame is not null)
            paletteFrame.Save(Path.Combine(OutDir, "s20-palette.png"), PngBitmapEncoderOptions.Default);
        paletteWindow.Close();
        Dispatcher.UIThread.RunJobs();

        vm.ShowTasksCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        Save(window, "s02-tasks-light.png");

        vm.ShowRemindersCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        Save(window, "s03-reminders-light.png");

        vm.ShowSettingsCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        Save(window, "s04-settings-light.png");

        // 设置页滚到底：字体区块（下拉 + 预览）在下面，单独留一张验收图。
        // SukiUI 的 SettingsLayout 把四个分组串在同一条滚动里，所以不是滚到底，
        // 而是把「预览」那一行顶到视口上方。
        var settingsScroller = window.GetVisualDescendants()
            .OfType<SettingsView>()
            .SelectMany(v => v.GetVisualDescendants().OfType<ScrollViewer>())
            .FirstOrDefault();
        var fontPreviewLabel = window.GetVisualDescendants()
            .OfType<TextBlock>()
            .FirstOrDefault(t => t.Text == "预览");
        if (settingsScroller is not null && fontPreviewLabel is not null)
        {
            var offset = fontPreviewLabel.TranslatePoint(new Point(0, 0), settingsScroller);
            settingsScroller.Offset = new Vector(
                settingsScroller.Offset.X,
                Math.Max(0, (offset?.Y ?? 0) - 120));
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
            Dispatcher.UIThread.RunJobs(DispatcherPriority.Background);
            window.UpdateLayout();
            Save(window, "s04c-settings-font.png");
            settingsScroller.Offset = new Vector(0, 0);
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
        }

        // 深色主题下选中「深色」卡片：验证金色描边 + 右上角对勾徽章
        vm.Settings!.SetThemeCommand.Execute("dark");
        vm.ApplyThemeVariant("dark");
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        Save(window, "s04b-settings-dark.png");
        vm.Settings!.SetThemeCommand.Execute("system");
        vm.ApplyThemeVariant("system");
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();

        // 把窗口压到最小宽度，确认左侧「外观/提醒/数据/关于」摘要导航不再被隐藏
        window.Width = 1024;
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs(DispatcherPriority.Background);
        Save(window, "s08-settings-narrow.png");
        window.Width = 1280;
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();

        vm.ApplyThemeVariant("dark");
        vm.ShowBoardCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        Save(window, "s05-board-dark.png");

        vm.ShowTasksCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        Save(window, "s06-tasks-dark.png");

        vm.ApplyThemeVariant("light");
        vm.ShowToast("已记下：把首页文案定下来", false);
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        // 提示有淡入动画，多跑一轮任务让它的首帧落地
        Dispatcher.UIThread.RunJobs(DispatcherPriority.Background);
        Save(window, "s07-toast.png");

        // 自定义主题色弹窗：内容视图单独截一张。
        // Suki 对话框在无头环境里入场动画不落地，只截主窗口会拍到背后的页面。
        var customThemeView = new CustomThemeDialogView { DataContext = vm.Settings, Width = 480 };
        using var themeWindow = new SukiWindow
        {
            Width = 480,
            Height = 380,
            Content = customThemeView,
            Title = "自定义主题色"
        };
        themeWindow.Show();
        Dispatcher.UIThread.RunJobs();
        themeWindow.UpdateLayout();
        Dispatcher.UIThread.RunJobs(DispatcherPriority.Background);
        themeWindow.UpdateLayout();
        Dispatcher.UIThread.RunJobs(DispatcherPriority.Background);

        var themeFrame = themeWindow.CaptureRenderedFrame();
        if (themeFrame is not null)
            themeFrame.Save(Path.Combine(OutDir, "s21-custom-theme-dialog.png"), PngBitmapEncoderOptions.Default);
        themeWindow.Close();
        Dispatcher.UIThread.RunJobs();

        // 套一套自定义配色：整窗（标题栏、按钮、强调线）应该一起换色
        vm.ShowSettingsCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        vm.Settings!.CustomPrimaryColor = Avalonia.Media.Color.Parse("#7A3FD0");
        vm.Settings.CustomAccentColor = Avalonia.Media.Color.Parse("#00B3A4");
        vm.Settings.ApplyCustomThemeCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs(DispatcherPriority.Background);
        window.UpdateLayout();
        Save(window, "s22-custom-theme-settings.png");

        // 自定义色下切深色：主色阶必须跟着重算，不能被内置色打回
        vm.ApplyThemeVariant("dark");
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs(DispatcherPriority.Background);
        window.UpdateLayout();
        Save(window, "s23-custom-theme-dark.png");
        vm.ApplyThemeVariant("light");
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();

        // 换字体：正文与浮层要一起变，不能只变一半
        vm.ApplyFont("Segoe UI");
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs(DispatcherPriority.Background);
        window.UpdateLayout();
        Save(window, "s24-custom-font.png");
        vm.ApplyFont(WorkStation.App.ViewModels.SettingsViewModel.DefaultFontName);
        vm.Settings.SetThemeColorCommand.Execute("Blue");
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();

        // 任务弹框（开始/截止两行日期 + 时刻）与项目弹框（起止日期必填）：
        // Suki 弹窗在无头环境里入场动画不落地，这里把模板内容单独塞进一个窗口截图。
        //
        // 这段必须排在所有页面截图之后：CaptureTemplate 会临时把 window.Content 换成模板内容，
        // 换回来之后主窗口在无头环境里就再也画不出东西（后面几张会全是白图）。
        // 同时关掉上一步的 Toast，避免它盖住弹窗底部的操作按钮。
        vm.IsToastVisible = false;
        window.Toasts.DismissAll();
        Settle(window);

        // 折叠态：确认外壳左列收窄、内容区跟着左移，且图标仍完整
        vm.ToggleSidebarCommand.Execute(null);
        Settle(window, 30);
        Save(window, "s19-menu-collapsed.png");
        vm.ToggleSidebarCommand.Execute(null);
        Settle(window, 30);

        // 选一个带起止区间的项目：任务的起止时间应自动继承项目
        var datedProject = service.ProjectsCreate(new CreateProjectInput
        {
            Name = "带区间的项目",
            StartDate = "2026-12-01",
            DueDate = "2026-12-20"
        }).Data!;
        // 项目下拉是页面刷新时取的：先建项目、再刷页面、最后开表单，选项才认得它
        vm.Tasks!.Reload();
        vm.Tasks.NewTaskCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();
        var taskForm = vm.Tasks.Form!;
        taskForm.ProjectIndex = Enumerable.Range(0, taskForm.ProjectChoices.Count)
            .First(i => taskForm.ProjectChoices[i].Id == datedProject.Id);
        Dispatcher.UIThread.RunJobs();
        CaptureTemplate(window, "TaskFormTemplate", taskForm, "s17-task-dialog.png");

        vm.NewProjectCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();
        CaptureTemplate(window, "ProjectFormTemplate", vm.Projects!.Form, "s18-project-dialog.png");
    }

    /// <summary>
    /// 推进无头渲染帧，让 SukiUI 的折叠/淡入动画真正走完再截图。
    /// 只 RunJobs 不会推进动画时钟，截出来会停在动画起点（比如折叠后图标还没滑进来）。
    /// </summary>
    private static void Settle(Window window, int frames = 16)
    {
        for (var i = 0; i < frames; i++)
        {
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            Dispatcher.UIThread.RunJobs();
        }

        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
    }

    private static void Save(Window window, string name)
    {
        var frame = window.CaptureRenderedFrame();
        if (frame is null) return;
        frame.Save(Path.Combine(OutDir, name), PngBitmapEncoderOptions.Default);
    }

    /// <summary>
    /// 把主窗口里的弹框模板单独渲染成一张图。
    ///
    /// 弹框本体在无头环境里截不到（入场动画不落地），所以把模板内容临时塞进主窗口：
    /// 主窗口是 Flat 背景，没有 Suki 的背景模糊，截图不会炸。
    /// </summary>
    private static void CaptureTemplate(
        Window owner,
        string templateKey,
        object? form,
        string name)
    {
        if (form is null) return;
        if (owner.Resources[templateKey] is not Avalonia.Controls.Templates.IDataTemplate template) return;

        var content = template.Build(form);
        if (content is null) return;

        // Build 出来的内容没有 DataContext（真机上由 ContentPresenter 兜底），
        // 这里手动补上，否则绑定全是空的，截出来的图是假象
        content.DataContext = form;

        var original = owner.Content;
        owner.Content = content;
        Dispatcher.UIThread.RunJobs();
        owner.UpdateLayout();
        Dispatcher.UIThread.RunJobs(DispatcherPriority.Background);
        owner.UpdateLayout();
        Save(owner, name);

        owner.Content = original;
        Dispatcher.UIThread.RunJobs();
        owner.UpdateLayout();
    }
}
