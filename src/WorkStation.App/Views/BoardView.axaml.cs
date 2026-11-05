using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using WorkStation.App.ViewModels;

namespace WorkStation.App.Views;

/// <summary>
/// 看板视图。这里只做两件事：把卡片上的指针动作翻译成拖拽会话，
/// 把列的落点翻译成一次 MoveTask / MoveProject 调用。
/// 状态迁移、提示与刷新都在 ViewModel 里，无头测试不需要模拟桌面拖拽。
/// </summary>
public partial class BoardView : UserControl
{
    /// <summary>进程内格式：只在本应用窗口之间传递标识，不需要跨进程序列化。</summary>
    private static readonly DataFormat<string> TaskFormat =
        DataFormat.CreateInProcessFormat<string>("workstation.board.task-id");

    private static readonly DataFormat<string> ProjectFormat =
        DataFormat.CreateInProcessFormat<string>("workstation.board.project-id");

    /// <summary>按下但还没超过拖动阈值的一次手势。等指针真的移动了才升级成拖拽。</summary>
    private PointerPressedEventArgs? _pendingPress;

    private Point _pendingOrigin;

    private string? _pendingId;

    private bool _pendingIsProject;

    /// <summary>拖动判定阈值（像素）。小于它视为点击，大于它才启动拖拽会话。</summary>
    private const double DragThreshold = 4;

    public BoardView()
    {
        InitializeComponent();

        // 指针移动与抬起挂在视图上：卡片自己只在按下时登记，避免卡片被模板复用后丢状态
        AddHandler(PointerMovedEvent, OnPointerMoved, RoutingStrategies.Tunnel | RoutingStrategies.Bubble, true);
        AddHandler(PointerReleasedEvent, OnPointerReleased, RoutingStrategies.Tunnel | RoutingStrategies.Bubble, true);
    }

    private BoardViewModel? ViewModel => DataContext as BoardViewModel;

    private void OnTaskCardPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Control control || control.DataContext is not BoardTaskCardViewModel card) return;
        if (!e.GetCurrentPoint(control).Properties.IsLeftButtonPressed) return;

        Register(e, card.Task.Id, isProject: false);
    }

    private void OnProjectCardPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Control control || control.DataContext is not BoardProjectCardViewModel card) return;
        if (!e.GetCurrentPoint(control).Properties.IsLeftButtonPressed) return;

        Register(e, card.Project.Id, isProject: true);
    }

    /// <summary>
    /// 只登记，不发起拖拽。按下就开拖会把双击、单击全部吞进拖拽会话，
    /// 卡片因此永远收不到 DoubleTapped。
    /// </summary>
    private void Register(PointerPressedEventArgs e, string id, bool isProject)
    {
        _pendingPress = e;
        _pendingOrigin = e.GetPosition(this);
        _pendingId = id;
        _pendingIsProject = isProject;
    }

    private void OnPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_pendingPress is not { } press) return;

        // 中途松开或换了按键：这次手势作废，不要留下一个会在下次移动时误触发的登记
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            ClearPending();
            return;
        }

        var point = e.GetPosition(this);
        var dx = point.X - _pendingOrigin.X;
        var dy = point.Y - _pendingOrigin.Y;
        if (dx * dx + dy * dy < DragThreshold * DragThreshold) return;

        var id = _pendingId;
        var isProject = _pendingIsProject;
        ClearPending();

        if (id is null) return;

        if (isProject)
        {
            BeginDrag(press, ProjectFormat, id);
        }
        else
        {
            BeginDrag(press, TaskFormat, id);
        }
    }

    private void OnPointerReleased(object? sender, PointerReleasedEventArgs e) => ClearPending();

    private void ClearPending()
    {
        _pendingPress = null;
        _pendingId = null;
        _pendingIsProject = false;
    }

    private static async void BeginDrag<T>(PointerPressedEventArgs e, DataFormat<T> format, T value)
        where T : class
    {
        var transfer = new DataTransfer();
        transfer.Add(DataTransferItem.Create(format, value));

        try
        {
            await DragDrop.DoDragDropAsync(e, transfer, DragDropEffects.Move);
        }
        catch (Exception error)
        {
            // 拖拽会话被系统取消（窗口失焦、Esc）时不需要打断用户，留一行日志即可
            Debug.WriteLine($"拖拽未完成：{error.Message}");
        }
    }

    private void OnTaskCardDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (sender is Control { DataContext: BoardTaskCardViewModel card })
        {
            ViewModel?.OpenTask(card.Task);
        }

        e.Handled = true;
    }

    private void OnTaskCardEditMenu(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { DataContext: BoardTaskCardViewModel card })
        {
            ViewModel?.OpenTask(card.Task);
        }
    }

    private void OnColumnDragOver(object? sender, DragEventArgs e)
    {
        if (sender is not Border column) return;

        var accepts = column.DataContext switch
        {
            BoardColumnViewModel => e.DataTransfer.Contains(TaskFormat),
            BoardProjectColumnViewModel => e.DataTransfer.Contains(ProjectFormat),
            _ => false
        };

        e.DragEffects = accepts ? DragDropEffects.Move : DragDropEffects.None;
        SetDragOver(column, accepts);
        e.Handled = true;
    }

    private void OnColumnDragLeave(object? sender, DragEventArgs e)
    {
        if (sender is Border column) SetDragOver(column, false);
    }

    private void OnColumnDrop(object? sender, DragEventArgs e)
    {
        if (sender is not Border column) return;

        SetDragOver(column, false);

        if (ViewModel is not { } vm) return;

        if (column.DataContext is BoardColumnViewModel taskColumn
            && e.DataTransfer.TryGetValue(TaskFormat) is { } taskId)
        {
            vm.MoveTask(taskId, taskColumn.Status);
            e.Handled = true;
        }
        else if (column.DataContext is BoardProjectColumnViewModel projectColumn
                 && e.DataTransfer.TryGetValue(ProjectFormat) is { } projectId)
        {
            vm.MoveProject(projectId, projectColumn.Status);
            e.Handled = true;
        }
    }

    private static void SetDragOver(Border column, bool value)
    {
        switch (column.DataContext)
        {
            case BoardColumnViewModel taskColumn:
                taskColumn.IsDragOver = value;
                break;
            case BoardProjectColumnViewModel projectColumn:
                projectColumn.IsDragOver = value;
                break;
        }
    }
}
