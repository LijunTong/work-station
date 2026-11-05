using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform;
using Avalonia.Threading;
using Avalonia.VisualTree;
using WorkStation.App.Services;
using WorkStation.App.ViewModels;

namespace WorkStation.App.Views;

public partial class MainWindow : Window
{
    private bool _forceClose;

    public MainWindow()
    {
        InitializeComponent();

        Icon = LoadWindowIcon();

        // 隧道阶段处理：等冒泡到窗口时，字母键往往已经被 TextBox 或列表吃掉了
        AddHandler(KeyDownEvent, OnWindowKeyDown, RoutingStrategies.Tunnel);

        Opened += OnOpened;
        Closing += OnClosing;

        // 真正关闭（退出）时停掉调度，否则进程退出前定时器还会再跑一拍
        Closed += (_, _) => ViewModel?.StopReminderPump();
    }

    public MainWindow(MainWindowViewModel viewModel) : this()
    {
        DataContext = viewModel;
    }

    /// <summary>
    /// 托盘是否真的建起来了。没有托盘时「关窗口驻留」必须退化成直接退出 ——
    /// 否则窗口一关，用户就再也找不回这个进程了。
    /// </summary>
    public bool TrayAvailable { get; set; }

    private MainWindowViewModel? ViewModel => DataContext as MainWindowViewModel;

    /// <summary>托盘菜单「退出」走这里：绕过驻留逻辑，真正关掉窗口。</summary>
    public void ForceClose()
    {
        _forceClose = true;
        Close();
    }

    private static WindowIcon? LoadWindowIcon()
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

    private void OnOpened(object? sender, EventArgs e)
    {
        if (ViewModel is not { IsReady: true } viewModel) return;

        try
        {
            viewModel.AttachNotifications(new WindowNotificationSink(this));
        }
        catch (Exception)
        {
            // 无头/无通知宿主的环境：没有通知通道，调度器照常把状态推进下去
        }

        viewModel.StartReminderPump();
    }

    private void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        if (_forceClose) return;
        if (!TrayAvailable) return;

        // 驻留托盘：窗口藏起来，提醒调度继续跑
        if (!(ViewModel?.CloseToTray ?? true)) return;

        e.Cancel = true;
        Hide();
    }

    private void OnWindowKeyDown(object? sender, KeyEventArgs e)
    {
        if (ViewModel is not { IsReady: true } vm) return;

        var ctrl = e.KeyModifiers.HasFlag(KeyModifiers.Control) || e.KeyModifiers.HasFlag(KeyModifiers.Meta);

        if (ctrl && e.Key is Key.K or Key.P)
        {
            if (vm.Palette is { IsOpen: true })
            {
                vm.Palette.Close();
            }
            else
            {
                vm.OpenPaletteCommand.Execute(null);
            }

            e.Handled = true;
            return;
        }

        // 面板自己是键盘的主人：上下、回车、Esc 都由它处理
        if (vm.Palette is { IsOpen: true }) return;

        if (e.Key == Key.Escape)
        {
            if (vm.IsShortcutHelpOpen)
            {
                vm.CloseShortcutHelpCommand.Execute(null);
                e.Handled = true;
            }

            return;
        }

        if (ctrl) return;

        // 正在输入时不要抢单个字母/数字 —— 在备注里打「n」不该跳去记录框
        if (FocusManager?.GetFocusedElement() is TextBox) return;

        switch (e.Key)
        {
            case Key.N:
                FocusQuickAdd();
                e.Handled = true;
                break;
            case Key.Oem2 when e.KeyModifiers.HasFlag(KeyModifiers.Shift):
                vm.ToggleShortcutHelpCommand.Execute(null);
                e.Handled = true;
                break;
            case Key.Oem2:
                FocusSearch();
                e.Handled = true;
                break;
            case Key.D1:
            case Key.NumPad1:
                vm.ShowBoardCommand.Execute(null);
                e.Handled = true;
                break;
            case Key.D2:
            case Key.NumPad2:
                vm.ShowTasksCommand.Execute(null);
                e.Handled = true;
                break;
            case Key.D3:
            case Key.NumPad3:
                vm.ShowProjectsCommand.Execute(null);
                e.Handled = true;
                break;
            case Key.D4:
            case Key.NumPad4:
                vm.ShowRemindersCommand.Execute(null);
                e.Handled = true;
                break;
            case Key.D5:
            case Key.NumPad5:
                vm.ShowSettingsCommand.Execute(null);
                e.Handled = true;
                break;
        }
    }

    private void FocusQuickAdd()
    {
        QuickAddBox.Focus();
        QuickAddBox.SelectAll();
    }

    private void FocusSearch()
    {
        if (TryFocusSearch()) return;

        // 项目页与设置页没有搜索框：先跳到任务页，等模板实例化后再聚焦
        ViewModel?.ShowTasksCommand.Execute(null);
        Dispatcher.UIThread.Post(() => TryFocusSearch(), DispatcherPriority.Background);
    }

    private bool TryFocusSearch()
    {
        foreach (var box in this.GetVisualDescendants().OfType<TextBox>())
        {
            if (box.Tag as string != "SearchBox") continue;

            box.Focus();
            box.SelectAll();
            return true;
        }

        return false;
    }
}
