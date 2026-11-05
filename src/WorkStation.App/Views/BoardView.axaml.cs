using System.Diagnostics;
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

    public BoardView() => InitializeComponent();

    private BoardViewModel? ViewModel => DataContext as BoardViewModel;

    private void OnTaskCardPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Control control || control.DataContext is not BoardTaskCardViewModel card) return;
        if (!e.GetCurrentPoint(control).Properties.IsLeftButtonPressed) return;

        BeginDrag(e, TaskFormat, card.Task.Id);
    }

    private void OnProjectCardPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Control control || control.DataContext is not BoardProjectCardViewModel card) return;
        if (!e.GetCurrentPoint(control).Properties.IsLeftButtonPressed) return;

        BeginDrag(e, ProjectFormat, card.Project.Id);
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
