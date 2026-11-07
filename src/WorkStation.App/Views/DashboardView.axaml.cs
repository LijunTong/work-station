using Avalonia.Controls;
using Avalonia.Input;
using WorkStation.App.ViewModels;

namespace WorkStation.App.Views;

public partial class DashboardView : UserControl
{
    public DashboardView() => InitializeComponent();

    /// <summary>
    /// 点项目行：弹该项目的任务清单。
    ///
    /// 这里没用 Button 做整行 —— SukiUI 的 Button 模板把内容塞进**横向** StackPanel，
    /// Grid 撑不满宽，右侧的「逾期」提示会紧贴在文字后面而不是靠右。
    /// 改成 Border 接 PointerPressed，布局完全由自己掌控。
    /// </summary>
    private void OnProjectRowPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Control control) return;
        if (control.DataContext is not DashboardProjectViewModel project) return;
        if (!e.GetCurrentPoint(control).Properties.IsLeftButtonPressed) return;

        ViewModel?.OpenProjectCommand.Execute(project);
    }

    /// <summary>点任务行：打开该任务的编辑弹窗（与看板双击卡片同一套弹窗）。</summary>
    private void OnTaskRowPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Control control) return;
        if (control.DataContext is not DashboardTaskViewModel task) return;
        if (!e.GetCurrentPoint(control).Properties.IsLeftButtonPressed) return;

        ViewModel?.OpenTaskCommand.Execute(task);
    }

    private DashboardViewModel? ViewModel => DataContext as DashboardViewModel;
}
