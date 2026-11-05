using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using WorkStation.App.ViewModels;

namespace WorkStation.App.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        // 隧道阶段处理：等冒泡到窗口时，字母键往往已经被 TextBox 或列表吃掉了
        AddHandler(KeyDownEvent, OnWindowKeyDown, RoutingStrategies.Tunnel);
    }

    public MainWindow(MainWindowViewModel viewModel) : this()
    {
        DataContext = viewModel;
    }

    private MainWindowViewModel? ViewModel => DataContext as MainWindowViewModel;

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
