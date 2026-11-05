using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using WorkStation.App.Platform;
using WorkStation.App.ViewModels;
using WorkStation.App.Views;

namespace WorkStation.App;

public partial class App : Application
{
    private AppServices? _services;
    private MainWindowViewModel? _viewModel;
    private TrayHost? _tray;

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            try
            {
                _services = AppServices.Start();
                _viewModel = new MainWindowViewModel(_services);

                var window = new MainWindow(_viewModel);
                desktop.MainWindow = window;

                // 托盘建不出来（少数 Linux 桌面、或图标资源读不到）不影响应用可用，
                // 只是关窗口就直接退出；原因交给窗口，首次打开时提示一次
                _tray = TrayHost.TryCreate(window, _viewModel, out var trayError);
                window.TrayAvailable = _tray is not null;
                window.TrayUnavailableReason = trayError;

                SingleInstance.ActivationRequested += () =>
                    Dispatcher.UIThread.Post(() => ActivateWindow(window, desktop));

                desktop.ShutdownRequested += (_, _) =>
                {
                    _viewModel?.StopReminderPump();
                    _tray?.Dispose();
                    _services?.Dispose();
                };
            }
            catch (Exception error)
            {
                // 数据库打不开时不能静默退出：Window 里有一块错误页把原因显示出来。
                // 这里不再 throw —— 抛出去只会让进程无声消失。
                desktop.MainWindow = new MainWindow(MainWindowViewModel.StartupFailed(error));
            }
        }

        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>第二个实例被启动时，把已有窗口从托盘里抬出来。</summary>
    private static void ActivateWindow(MainWindow window, IClassicDesktopStyleApplicationLifetime desktop)
    {
        if (desktop.MainWindow is null) return;

        window.ShowInTaskbar = true;
        window.Show();

        if (window.WindowState == WindowState.Minimized)
        {
            window.WindowState = WindowState.Normal;
        }

        window.Activate();
    }
}
