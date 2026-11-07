using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using SukiUI.Controls;
using WorkStation.App.ViewModels;

namespace WorkStation.App.Views;

/// <summary>
/// 项目任务弹窗的内容。布局在 XAML 里（左：项目档案 / 右：垂直 stepper + 快速添加）。
///
/// SukiUI 的 VerticalStepper 用 ToString() 渲染每一步的文本、用 Index 高亮「当前步」，
/// 并不消费 VerticalStepperItem 的 Title/Description/状态色。所以这里直接给 Steps 喂
/// 「标题 · 状态描述」字符串（状态文字已在 Description 里：待办/进行中/已完成/已逾期…），
/// 并把「第一件还没做完的事」设为 Index 作为当前步 —— stepper 按位置画对勾，
/// 清单里已完成的任务排在最前，对勾正好落在它们身上。
///
/// 数据一变就**换一个新的 VerticalStepper 实例**（见 RebuildStepper 里的注释），
/// 不原地改同一个控件的 Steps。
/// </summary>
public partial class ProjectTasksDialogView : UserControl
{
    private ProjectTasksDialogViewModel? _vm;

    public ProjectTasksDialogView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object? sender, System.EventArgs e)
    {
        if (_vm is not null) _vm.StepsRebuilt -= OnStepsRebuilt;
        _vm = DataContext as ProjectTasksDialogViewModel;
        if (_vm is not null) _vm.StepsRebuilt += OnStepsRebuilt;
        RebuildStepper();
    }

    private void OnStepsRebuilt() => RebuildStepper();

    private void RebuildStepper()
    {
        if (_vm is null) return;

        var steps = _vm.Steps;
        var items = new List<string>(steps.Count);
        var current = -1;
        for (var i = 0; i < steps.Count; i++)
        {
            var s = steps[i];
            items.Add(string.IsNullOrEmpty(s.Description) ? s.Title : $"{s.Title} · {s.Description}");

            // SukiUI 的 stepper 按**位置**画对勾：当前步之前的都算「已完成」。
            // 清单把已完成的任务排在最前，所以当前步取「第一件还没做完的事」——
            // 对勾正好落在已完成的那几步上，不会给没做完的任务盖对勾。
            if (current < 0 && !s.IsCompleted) current = i;
        }

        // 全部完成时把最后一步设为当前步（没有待办就没有对勾）
        if (current < 0) current = Math.Max(0, steps.Count - 1);

        // 为什么每次换一个新控件，而不是给同一个控件重新赋 Steps：
        //
        // VerticalStepper.RefreshSteps() 里的 _stepItems 是**先 Clear() 再重新填充的同一个
        // List 实例**，填完又把它赋回 ItemsSource。引用没变 → ItemsControl 认为数据没动，
        // 旧容器不会被清掉；实测「加一条任务」之后，清单里会变成「旧行 + 新行」三行。
        // 而设 Steps = null 再设回来只能把 ItemsSource 置空一帧，时机一变就不可靠。
        //
        // 新实例的模板第一次应用时 ItemsSource 是从 null 变成列表，属于干净重建，
        // 不会有残留容器。代价是重画一次清单，对这个量级的弹窗没有影响。
        var stepper = new VerticalStepper { ShowCheckMark = true };

        StepperHost.Children.Clear();
        StepperHost.Children.Add(stepper);

        // 挂到树上之后再赋值：此时模板已应用，_itemsControl 不为 null，赋值即重建
        stepper.Steps = items;
        stepper.Index = current;
    }
}
