using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform;
using CommunityToolkit.Mvvm.Input;
using WorkStation.App.ViewModels;
using WorkStation.App.Views;

namespace WorkStation.App.Platform;

/// <summary>
/// 托盘常驻：关窗口不等于退出，提醒才有机会「到点弹出来」。
///
/// 菜单只放四件事：回到窗口、今天的看板、未处理提醒、退出。
/// 托盘菜单不是第二个应用，塞满导航项只会让人在两层导航之间犹豫。
/// </summary>
internal sealed class TrayHost : IDisposable
{
    private readonly TrayIcon _icon;
    private readonly MainWindow _window;
    private readonly MainWindowViewModel _viewModel;
    private readonly NativeMenuItem _todayItem;
    private readonly NativeMenuItem _remindersItem;

    private TrayHost(TrayIcon icon, MainWindow window, MainWindowViewModel viewModel,
        NativeMenuItem todayItem, NativeMenuItem remindersItem)
    {
        _icon = icon;
        _window = window;
        _viewModel = viewModel;
        _todayItem = todayItem;
        _remindersItem = remindersItem;

        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        Update();
    }

    /// <summary>
    /// 托盘在部分 Linux 桌面环境（无 StatusNotifier 主机）里建不出来。
    /// 那种情况下安静地跳过：应用照常可用，只是关窗口就退出。
    ///
    /// <paramref name="error"/> 把失败原因交回去 —— 早先这里 catch 完什么都不说，
    /// 用户只看到「没有图标」，分不清是系统不支持还是图标没加载出来。
    /// </summary>
    public static TrayHost? TryCreate(MainWindow window, MainWindowViewModel viewModel, out string? error)
    {
        try
        {
            // 图标读不出来就当作建不出来：Avalonia 允许 TrayIcon.Icon 为 null，
            // 那样会在通知区域留下一个**看不见的**托盘图标，比没有更糟
            var icon = LoadIcon();
            if (icon is null)
            {
                error = "托盘图标资源读取失败";
                return null;
            }

            var todayItem = new NativeMenuItem("今日看板") { Command = new RelayCommand(() => Activate(window, () => viewModel.ShowBoardCommand.Execute(null))) };
            var remindersItem = new NativeMenuItem("未处理提醒") { Command = new RelayCommand(() => Activate(window, () => viewModel.ShowRemindersCommand.Execute(null))) };

            var menu = new NativeMenu();
            menu.Items.Add(new NativeMenuItem("打开工作台") { Command = new RelayCommand(() => Activate(window, null)) });
            menu.Items.Add(todayItem);
            menu.Items.Add(remindersItem);
            menu.Items.Add(new NativeMenuItemSeparator());
            menu.Items.Add(new NativeMenuItem("退出") { Command = new RelayCommand(Exit) });

            var trayIcon = new TrayIcon
            {
                Icon = icon,
                ToolTipText = "工作台",
                Menu = menu,
                IsVisible = true
            };

            trayIcon.Clicked += (_, _) => Activate(window, null);

            error = null;
            return new TrayHost(trayIcon, window, viewModel, todayItem, remindersItem);
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return null;
        }
    }

    private static WindowIcon? LoadIcon()
    {
        try
        {
            return new WindowIcon(AssetLoader.Open(new Uri("avares://WorkStation/Assets/app.png")));
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static void Activate(MainWindow window, Action? after)
    {
        window.ShowInTaskbar = true;
        window.Show();

        if (window.WindowState == WindowState.Minimized)
        {
            window.WindowState = WindowState.Normal;
        }

        window.Activate();
        after?.Invoke();
    }

    private static void Exit()
    {
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.Shutdown();
        }
    }

    private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(MainWindowViewModel.ReminderCount) or nameof(MainWindowViewModel.OverdueCount))
        {
            Update();
        }
    }

    private void Update()
    {
        var unread = _viewModel.ReminderCount;
        var overdue = _viewModel.OverdueCount;

        _remindersItem.Header = unread > 0 ? $"未处理提醒（{unread}）" : "未处理提醒";
        _remindersItem.IsEnabled = unread > 0;
        _todayItem.Header = overdue > 0 ? $"今日看板（逾期 {overdue}）" : "今日看板";

        _icon.ToolTipText = unread > 0 || overdue > 0
            ? $"工作台 · {unread} 条提醒待处理 · {overdue} 条逾期"
            : "工作台";
    }

    public void Dispose()
    {
        _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        _icon.IsVisible = false;
        _icon.Dispose();
    }
}
