using System.Linq;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using SukiUI.Controls;
using WorkStation.App.ViewModels;
using WorkStation.App.Views;
using WorkStation.Core.Models;
using WorkStation.Core.Services;
using Xunit;

namespace WorkStation.Tests;

/// <summary>
/// 项目任务弹窗里的清单（SukiUI VerticalStepper）必须跟着数据走。
///
/// 这是个已经踩过的坑：VerticalStepper 刷新时复用的是**同一个 List 实例**，
/// 赋回 ItemsSource 后引用没变，ItemsControl 就认为数据没动 —— 表现为
/// 「快速添加一条任务，条数徽标变了，清单还是原来的行」。所以这里守住行数。
/// </summary>
public class ProjectTasksDialogStepperTests
{
    [AvaloniaFact]
    public void QuickAdd_RebuildsStepperRows()
    {
        using var db = TestDb.OpenMigrated();
        var service = new WorkStationService(db, ":memory:");
        var project = service.ProjectsCreate(new CreateProjectInput { Name = "验收项目" }).Data!;
        service.TasksCreate(new CreateTaskInput { Title = "已有", ProjectId = project.Id });

        var vm = new ProjectTasksDialogViewModel(project.Id, service, (_, _) => { });
        var view = new ProjectTasksDialogView { DataContext = vm };
        var window = new Window { Width = 820, Height = 560, Content = view };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(["已有"], Rows(view));

        vm.QuickAddText = "新加一条";
        vm.AddTasksCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();

        // 不多不少：既不能漏掉新行（旧 bug），也不能把旧行留在树里
        Assert.Equal(2, Rows(view).Count);
        Assert.Contains("新加一条", Rows(view));

        window.Close();
    }

    private static System.Collections.Generic.List<string> Rows(ProjectTasksDialogView view) =>
        view.GetVisualDescendants()
            .OfType<VerticalStepperItem>()
            .Select(i => i.Title ?? string.Empty)
            .ToList();
}
