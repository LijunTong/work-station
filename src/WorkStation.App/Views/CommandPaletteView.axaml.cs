using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using WorkStation.App.ViewModels;

namespace WorkStation.App.Views;

/// <summary>
/// 命令面板视图。键盘交互放在这里：焦点在搜索框里，
/// 上下键与回车要在冒泡前截住，否则会被 TextBox 当成光标移动。
/// </summary>
public partial class CommandPaletteView : UserControl
{
    private CommandPaletteViewModel? _viewModel;

    public CommandPaletteView()
    {
        InitializeComponent();
        AddHandler(KeyDownEvent, OnPaletteKeyDown, RoutingStrategies.Tunnel);
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);

        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        }

        _viewModel = DataContext as CommandPaletteViewModel;

        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(CommandPaletteViewModel.IsOpen) || _viewModel is not { IsOpen: true }) return;

        // 面板刚变可见时控件还没完成布局，直接 Focus 会被忽略
        Dispatcher.UIThread.Post(
            () =>
            {
                SearchBox.Focus();
                SearchBox.SelectAll();
            },
            DispatcherPriority.Background);
    }

    private void OnPaletteKeyDown(object? sender, KeyEventArgs e)
    {
        if (_viewModel is not { IsOpen: true }) return;

        switch (e.Key)
        {
            case Key.Down:
                _viewModel.MoveSelection(1);
                e.Handled = true;
                break;
            case Key.Up:
                _viewModel.MoveSelection(-1);
                e.Handled = true;
                break;
            case Key.Enter:
                _viewModel.RunSelected();
                e.Handled = true;
                break;
            case Key.Escape:
                _viewModel.Close();
                e.Handled = true;
                break;
        }
    }
}
