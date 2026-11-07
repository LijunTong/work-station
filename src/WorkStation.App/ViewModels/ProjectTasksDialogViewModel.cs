using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WorkStation.Core;
using WorkStation.Core.Models;
using WorkStation.Core.Services;
using WorkStation.Core.Time;

namespace WorkStation.App.ViewModels;

/// <summary>
/// 从项目卡片「查看任务」进来的弹窗内容：不再跳转到任务页，
/// 直接在一个浮层里展示该项目的任务清单（垂直 stepper）+ 项目整体进度（放大的圆形进度条）。
/// 项目卡片头信息直接复用 ProjectCardViewModel，避免重复算进度/状态文案。
/// </summary>
public sealed partial class ProjectTasksDialogViewModel : ObservableObject
{
    private readonly WorkStationService _service;
    private readonly Action<string, bool> _toast;
    private readonly Func<DateTime> _clock;

    public ProjectTasksDialogViewModel(
        string projectId,
        WorkStationService service,
        Action<string, bool> toast,
        Func<DateTime>? clock = null)
    {
        _service = service;
        _toast = toast;
        _clock = clock ?? (() => DateTime.Now);
        Load(projectId);
    }

    public string ProjectId { get; private set; } = string.Empty;

    public ProjectCardViewModel? Card { get; private set; }

    public ObservableCollection<ProjectTaskStep> Steps { get; } = [];

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string? _error;

    public bool HasError => Error is not null;

    public bool HasCard => Card is not null;

    public event Action? CloseRequested;

    [RelayCommand]
    private void Close() => CloseRequested?.Invoke();

    [ObservableProperty]
    private string _quickAddText = string.Empty;

    /// <summary>步骤数据重建后通知视图刷新 VerticalStepper（其数据模型是控件，须在视图侧重建）。</summary>
    public event Action? StepsRebuilt;

    /// <summary>
    /// 快速添加：输入框里每行一个任务，一次可加多个；全部建为「待办」，归属当前项目。
    /// 建完清空输入框、重建清单与进度。
    /// </summary>
    [RelayCommand]
    private void AddTasks()
    {
        var titles = QuickAddText
            .Split('\n')
            .Select(l => l.Trim().TrimEnd('\r'))
            .Where(l => l.Length > 0)
            .ToList();
        if (titles.Count == 0) return;

        var created = 0;
        foreach (var title in titles)
        {
            if (_service.TasksCreate(new CreateTaskInput
                {
                    Title = title,
                    ProjectId = ProjectId,
                    Status = TaskStatus.Todo,
                    // 项目内的任务默认落在项目的起止区间里
                    StartAt = ProjectStartAt,
                    DueAt = ProjectDueAt
                }).Ok)
            {
                created++;
            }
        }

        QuickAddText = string.Empty;
        if (created > 0) _toast($"已添加 {created} 个任务", false);
        Load(ProjectId);
    }

    /// <summary>项目开始日期对应的墙钟（当天 00:00）。项目没设开始日期时为空。</summary>
    private string? ProjectStartAt { get; set; }

    /// <summary>项目结束日期对应的墙钟（当天 18:00）。项目没设结束日期时为空。</summary>
    private string? ProjectDueAt { get; set; }

    private void Load(string projectId)
    {
        IsLoading = true;
        try
        {
            var projectResult = _service.ProjectsGet(projectId);
            if (!projectResult.Ok || projectResult.Data is null)
            {
                Error = projectResult.Error?.Message ?? "项目不存在";
                return;
            }

            var tasksResult = _service.TasksList(new TaskFilter
            {
                ProjectId = Optional<string?>.Some(projectId)
            });
            var tasks = tasksResult.Ok ? tasksResult.Data ?? [] : [];

            if (!tasksResult.Ok)
            {
                _toast("任务加载失败，清单可能不完整", true);
            }

            Card = ProjectCardViewModel.Create(
                projectResult.Data,
                tasks,
                _ => { },
                _ => { },
                _ => { },
                _ => { });
            ProjectId = projectId;

            // 项目起止 → 项目内任务的默认起止
            ProjectStartAt = ToWallClock(projectResult.Data.StartDate, TimeSpan.Zero);
            ProjectDueAt = ToWallClock(projectResult.Data.DueDate, new TimeSpan(18, 0, 0));

            BuildSteps(tasks, _clock());
        }
        finally
        {
            IsLoading = false;
            OnPropertyChanged(nameof(Card));
            OnPropertyChanged(nameof(HasCard));
            OnPropertyChanged(nameof(HasError));
        }
    }

    /// <summary>项目日期（yyyy-MM-dd）转成墙钟（yyyy-MM-ddTHH:mm）。</summary>
    private static string? ToWallClock(string? date, TimeSpan time)
    {
        if (string.IsNullOrWhiteSpace(date)) return null;
        if (!DateTime.TryParseExact(
                date.Trim(),
                "yyyy-MM-dd",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var parsed))
        {
            return null;
        }

        return LocalTime.ToLocalDateTime(parsed.Date.Add(time));
    }

    /// <summary>
    /// 步骤顺序：**已完成的排在最前面**，组内按项目里的原始顺序；
    /// 未完成的排在后面，逾期的优先、其次按截止时间。
    ///
    /// 为什么完成的在前：stepper 是一条竖线，已完成的步骤连在一起才看得出
    /// 「这一段已经走完了」。1、2、3 里完成了 1 和 3，那就让 1、3 挨着排前面，
    /// 中间的 2 落在后面 —— 连起来的那条线才是连续的。
    /// </summary>
    private void BuildSteps(IReadOnlyList<TaskItem> tasks, DateTime now)
    {
        Steps.Clear();

        var nowLocal = LocalTime.ToLocalDateTime(now);

        // 带上原始下标做最后的排序依据：
        // created_at 只到毫秒，同一批创建的任务时间戳可能一模一样，
        // 光按时间排会随机掉换顺序（「1、3」变成「3、1」）。
        var indexed = tasks.Select((task, index) => (Task: task, Index: index)).ToList();

        var finished = indexed
            .Where(x => IsFinished(x.Task))
            .OrderBy(x => x.Task.CreatedAt, StringComparer.Ordinal)
            .ThenBy(x => x.Index);

        var pending = indexed
            .Where(x => !IsFinished(x.Task))
            // 待办部分与其它页面同一套口径：优先级 → 逾期 → 截止时间
            .OrderBy(x => x.Task, TaskOrdering.OpenTaskComparer(now))
            .ThenBy(x => x.Index);

        foreach (var entry in finished.Concat(pending))
        {
            var task = entry.Task;
            var overdue = !IsFinished(task) && Formatting.IsOverdue(task.DueAt, nowLocal);

            Steps.Add(new ProjectTaskStep
            {
                Title = task.Title,
                Description = BuildDescription(task, overdue),
                IsCompleted = IsFinished(task),
                IsActive = task.Status == TaskStatus.Doing
            });
        }

        StepsRebuilt?.Invoke();
    }

    /// <summary>已完成或已取消：都算「这一步走完了」。</summary>
    private static bool IsFinished(TaskItem task) =>
        task.Status is TaskStatus.Done or TaskStatus.Canceled;

    private static string BuildDescription(TaskItem task, bool overdue)
    {
        var parts = new List<string>();
        if (overdue) parts.Add("已逾期");
        if (!string.IsNullOrEmpty(task.StartAt)) parts.Add($"起 {Formatting.FormatLocalDateTime(task.StartAt)}");
        if (!string.IsNullOrEmpty(task.DueAt)) parts.Add($"截止 {Formatting.FormatLocalDateTime(task.DueAt)}");

        var status = task.Status switch
        {
            TaskStatus.Todo => "待办",
            TaskStatus.Doing => "进行中",
            TaskStatus.Blocked => "已阻塞",
            TaskStatus.Done => "已完成",
            _ => "已取消"
        };

        // 已完成/已取消、进行中、已阻塞都写出来；「待办」是默认状态，不必占位置
        if (status is "已完成" or "已取消" or "进行中" or "已阻塞") parts.Add(status);

        return string.Join(" · ", parts);
    }
}

/// <summary>任务清单里的一个步骤（喂给 SukiUI 的 VerticalStepper）。纯数据，不含 UI 控件。</summary>
public sealed class ProjectTaskStep
{
    public string Title { get; init; } = string.Empty;

    public string? Description { get; init; }

    public bool IsCompleted { get; init; }

    public bool IsActive { get; init; }

    public bool IsPending => !IsCompleted && !IsActive;
}
