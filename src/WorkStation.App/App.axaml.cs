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
    private TrayIcons? _trayIcons;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);

        // SukiUI 7.0.1 的缺陷：XAML 里设 ThemeColor 走的是 Avalonia 的 SetValue，不会经过
        // CLR 属性 setter，也就不会触发主色资源注册 —— SukiPrimaryColor / 75 / 10 这些键
        // 根本不存在，引用它们的强调色会全部解析失败。必须在代码里显式「重新应用一次
        // 当前主题色」（不选色，用的就是默认值），键才会写入。
        // （main 分支已把注册挪进构造函数，7.0.1 没有；升级 SukiUI 后这一段可删。）
        var sukiTheme = SukiUI.SukiTheme.GetInstance(Application.Current!);
        sukiTheme.ChangeColorTheme(sukiTheme.ThemeColor);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            try
            {
                _services = AppServices.Start();

                // 日志与数据库同级，出问题时用户只需要把 .data/startup.log 发回来
                AppLog.Init(Path.GetDirectoryName(_services.DatabasePath) ?? AppContext.BaseDirectory);
                AppLog.Write($"启动 {typeof(App).Assembly.GetName().Version} db={_services.DatabasePath}");

                _viewModel = new MainWindowViewModel(_services);

                var window = new MainWindow(_viewModel);
                desktop.MainWindow = window;

                // 托盘建不出来（少数 Linux 桌面、或图标资源读不到）不影响应用可用，
                // 只是关窗口就直接退出；原因交给窗口，首次打开时提示一次
                //
                // 这个集合必须先挂到 Application 上：Avalonia 只对集合里的 TrayIcon
                // 创建原生实现，光 new 一个 TrayIcon 再设 IsVisible 是不会出现在通知区域的
                _trayIcons = new TrayIcons();
                TrayIcon.SetIcons(this, _trayIcons);

                _tray = TrayHost.TryCreate(window, _viewModel, _trayIcons, out var trayError);
                window.TrayAvailable = _tray is not null;
                window.TrayUnavailableReason = trayError;
                AppLog.Write($"托盘：可用={_tray is not null} 原因={trayError ?? "无"}");

                SingleInstance.ActivationRequested += () =>
                    Dispatcher.UIThread.Post(() => ActivateWindow(window, desktop));

                desktop.ShutdownRequested += (_, _) =>
                {
                    AppLog.Write("退出：停调度、销毁托盘");
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
