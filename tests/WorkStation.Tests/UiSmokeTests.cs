using Avalonia;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Microsoft.Data.Sqlite;
using WorkStation.App.ViewModels;
using WorkStation.App.Views;
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
    public void MainWindow_ShowsProjectsPage_ThenNavigates()
    {
        var vm = new MainWindowViewModel(_service);
        var window = new MainWindow(vm);

        window.Show();
        Dispatcher.UIThread.RunJobs();

        Assert.True(window.IsVisible);
        Assert.IsType<ProjectsViewModel>(vm.CurrentPage);
        Assert.Equal("项目", vm.PageTitle);

        vm.ShowTasksCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();

        Assert.IsType<TasksViewModel>(vm.CurrentPage);
        Assert.Equal("任务", vm.PageTitle);

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
