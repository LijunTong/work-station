using System.Collections.ObjectModel;
using System.Globalization;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WorkStation.Core;
using WorkStation.Core.Models;
using WorkStation.Core.Services;
using WorkStation.Core.Time;

namespace WorkStation.App.ViewModels;

/// <summary>
/// 任务页：快速添加 + 日期分组 + 筛选。
///
/// 分组口径全部来自 Core 的 TaskGrouping，页面不自己算 —— Electron 版把 groupOf
/// 塞在组件里，周日边界算错过一次，且没有任何测试能发现。
/// </summary>
public partial class TasksViewModel : ObservableObject
{
    private readonly WorkStationService _service;
    private readonly Action<string, bool> _toast;
    private readonly Func<DateTime> _clock;

    /// <summary>折叠状态跨刷新保留：重新加载不该把用户折叠的组又展开。</summary>
    private readonly HashSet<string> _collapsed = [];

    public TasksViewModel(WorkStationService service, Action<string, bool> toast, Func<DateTime>? clock = null)
    {
        _service = service;
        _toast = toast;
        _clock = clock ?? (() => DateTime.Now);

        StatusFilters =
        [
            new StatusFilter("all", "全部状态", null),
            new StatusFilter("todo", "待办", TaskStatus.Todo),
            new StatusFilter("doing", "进行中", TaskStatus.Doing),
            new StatusFilter("blocked", "已阻塞", TaskStatus.Blocked),
            new StatusFilter("done", "已完成", TaskStatus.Done),
            new StatusFilter("canceled", "已取消", TaskStatus.Canceled)
        ];
        _selectedStatusFilter = StatusFilters[0];
    }

    public ObservableCollection<TaskGroupViewModel> Groups { get; } = [];

    public ObservableCollection<ProjectOption> ProjectOptions { get; } = [];

    public IReadOnlyList<StatusFilter> StatusFilters { get; }

    [ObservableProperty]
    private StatusFilter _selectedStatusFilter;

    [ObservableProperty]
    private ProjectOption? _selectedProject;

    [ObservableProperty]
    private string _quickAddTitle = string.Empty;

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string? _error;

    [ObservableProperty]
    private bool _isFormOpen;

    [ObservableProperty]
    private TaskFormViewModel? _form;

    [ObservableProperty]
    private TaskRowViewModel? _pendingDelete;

    public bool IsDeleteConfirmOpen => PendingDelete is not null;

    public bool HasTasks => Groups.Count > 0;

    public bool ShowEmpty => !IsLoading && Error is null && Groups.Count == 0;

    public bool HasError => Error is not null;

    partial void OnSelectedStatusFilterChanged(StatusFilter value) => Reload();

    partial void OnSelectedProjectChanged(ProjectOption? value) => Reload();

    partial void OnSearchTextChanged(string value) => Reload();

    partial void OnErrorChanged(string? value)
    {
        OnPropertyChanged(nameof(HasError));
        OnPropertyChanged(nameof(ShowEmpty));
    }

    partial void OnIsLoadingChanged(bool value) => OnPropertyChanged(nameof(ShowEmpty));

    partial void OnPendingDeleteChanged(TaskRowViewModel? value) =>
        OnPropertyChanged(nameof(IsDeleteConfirmOpen));

    public void Reload()
    {
        IsLoading = true;
        try
        {
            var projectsResult = _service.ProjectsList(includeArchived: true);
            var projectNames = (projectsResult.Data ?? [])
                .ToDictionary(p => p.Id, p => p.Name);

            ReloadProjectOptions(projectsResult.Data ?? []);

            var filter = new TaskFilter();
            if (SelectedStatusFilter.Status is { } status)
            {
                filter = filter with { Status = [status] };
            }

            if (SelectedProject is { Id: not null } selected)
            {
                filter = filter with { ProjectId = Optional<string?>.Some(selected.Id) };
            }

            if (!string.IsNullOrWhiteSpace(SearchText))
            {
                filter = filter with { Search = SearchText.Trim() };
            }

            var result = _service.TasksList(filter);
            if (!result.Ok || result.Data is null)
            {
                Error = result.Error?.Message ?? "任务加载失败";
                Groups.Clear();
                return;
            }

            Error = projectsResult.Ok ? null : "项目列表加载失败，任务上的项目名可能显示为「未知项目」";
            BuildGroups(result.Data, projectNames);
        }
        finally
        {
            IsLoading = false;
            OnPropertyChanged(nameof(HasTasks));
            OnPropertyChanged(nameof(ShowEmpty));
        }
    }

    private void ReloadProjectOptions(IReadOnlyList<Project> projects)
    {
        var previous = SelectedProject?.Id;

        ProjectOptions.Clear();
        ProjectOptions.Add(ProjectOption.All);
        foreach (var project in projects)
        {
            var archived = project.ArchivedAt is not null;
            ProjectOptions.Add(new ProjectOption(
                project.Id,
                archived ? $"{project.Name}（已归档）" : project.Name,
                archived));
        }

        // 重建选项后恢复原选中项；找不到就回落到「全部项目」
        SelectedProject = ProjectOptions.FirstOrDefault(o => o.Id == previous) ?? ProjectOptions[0];
    }

    private void BuildGroups(IReadOnlyList<TaskItem> tasks, IReadOnlyDictionary<string, string> projectNames)
    {
        var now = _clock();
        var rows = tasks.Select(t => new TaskRowViewModel(
            t,
            t.ProjectId is not null && projectNames.TryGetValue(t.ProjectId, out var name) ? name : null,
            now,
            CompleteTask,
            EditTask,
            RequestDelete)).ToList();

        var open = rows.Where(r => !r.IsFinished).ToList();
        var finished = rows.Where(r => r.IsFinished)
            .OrderByDescending(r => r.Task.CompletedAt ?? r.Task.UpdatedAt)
            .ToList();

        Groups.Clear();

        AddGroup("today", "今日", open.Where(r => r.Group == Core.TaskGroup.Today)
            .OrderBy(r => r.Task.DueAt, StringComparer.Ordinal));
        AddGroup("overdue", "逾期", open.Where(r => r.Group == Core.TaskGroup.Overdue)
            .OrderBy(r => r.Task.DueAt, StringComparer.Ordinal));
        AddGroup("week", "本周", open.Where(r => r.Group == Core.TaskGroup.Week)
            .OrderBy(r => r.Task.DueAt, StringComparer.Ordinal));
        AddGroup("later", "以后", open.Where(r => r.Group == Core.TaskGroup.Later)
            .OrderBy(r => r.Task.DueAt, StringComparer.Ordinal));
        AddGroup("someday", "无期限", open.Where(r => r.Group == Core.TaskGroup.Someday)
            .OrderBy(r => r.Task.CreatedAt, StringComparer.Ordinal));
        AddGroup("finished", "已完结", finished);
    }

    private void AddGroup(string key, string title, IEnumerable<TaskRowViewModel> rows)
    {
        var items = rows.ToList();
        if (items.Count == 0) return;

        var group = new TaskGroupViewModel(key, title, items, ToggleGroup)
        {
            IsExpanded = !_collapsed.Contains(key)
        };
        Groups.Add(group);
    }

    private void ToggleGroup(TaskGroupViewModel group)
    {
        group.IsExpanded = !group.IsExpanded;
        if (group.IsExpanded)
        {
            _collapsed.Remove(group.Key);
        }
        else
        {
            _collapsed.Add(group.Key);
        }
    }

    [RelayCommand]
    private void QuickAdd()
    {
        var title = QuickAddTitle.Trim();
        if (title.Length == 0) return;

        var result = _service.TasksCreate(new CreateTaskInput { Title = title });
        if (!result.Ok)
        {
            _toast(result.Error?.Message ?? "添加失败", true);
            return;
        }

        QuickAddTitle = string.Empty;
        Reload();
    }

    [RelayCommand]
    private void NewTask()
    {
        Form = TaskFormViewModel.ForCreate(ProjectOptions, SaveForm, CloseForm);
        IsFormOpen = true;
    }

    [RelayCommand]
    private void CloseForm() => IsFormOpen = false;

    [RelayCommand]
    private void SaveForm()
    {
        if (Form is not { } form) return;
        if (!form.Validate())
        {
            return;
        }

        if (form.IsEditing)
        {
            var updated = _service.TasksUpdate(form.TaskId!, form.BuildUpdate());
            if (!updated.Ok)
            {
                form.Error = updated.Error?.Message ?? "保存失败";
                return;
            }
        }
        else
        {
            var created = _service.TasksCreate(form.BuildCreate());
            if (!created.Ok)
            {
                form.Error = created.Error?.Message ?? "保存失败";
                return;
            }
        }

        IsFormOpen = false;
        _toast(form.IsEditing ? "任务已更新" : "任务已创建", false);
        Reload();
    }

    private void CompleteTask(TaskRowViewModel row)
    {
        var result = _service.TasksSetStatus(row.Task.Id, TaskStatus.Done);
        if (!result.Ok)
        {
            _toast(result.Error?.Message ?? "操作失败", true);
            return;
        }

        Reload();
    }

    private void EditTask(TaskRowViewModel row)
    {
        Form = TaskFormViewModel.ForEdit(row.Task, ProjectOptions, SaveForm, CloseForm);
        IsFormOpen = true;
    }

    private void RequestDelete(TaskRowViewModel row) => PendingDelete = row;

    [RelayCommand]
    private void CancelDelete() => PendingDelete = null;

    [RelayCommand]
    private void ConfirmDelete()
    {
        if (PendingDelete is not { } row) return;

        var result = _service.TasksRemove(row.Task.Id);
        PendingDelete = null;

        if (!result.Ok)
        {
            _toast(result.Error?.Message ?? "删除失败", true);
            return;
        }

        _toast("任务已删除", false);
        Reload();
    }

    [RelayCommand]
    private void ClearFilters()
    {
        SearchText = string.Empty;
        SelectedStatusFilter = StatusFilters[0];
        SelectedProject = ProjectOptions.FirstOrDefault();
    }
}

public sealed record StatusFilter(string Key, string Label, TaskStatus? Status);

public sealed record ProjectOption(string? Id, string Name, bool IsArchived = false)
{
    public static ProjectOption All { get; } = new(null, "全部项目");

    public static ProjectOption Standalone { get; } = new(null, "独立任务");
}

public partial class TaskGroupViewModel : ObservableObject
{
    private readonly Action<TaskGroupViewModel> _toggle;

    public TaskGroupViewModel(
        string key,
        string title,
        IReadOnlyList<TaskRowViewModel> tasks,
        Action<TaskGroupViewModel> toggle)
    {
        Key = key;
        Title = title;
        Tasks = new ObservableCollection<TaskRowViewModel>(tasks);
        _toggle = toggle;
    }

    public string Key { get; }

    public string Title { get; }

    public ObservableCollection<TaskRowViewModel> Tasks { get; }

    [ObservableProperty]
    private bool _isExpanded = true;

    public int Count => Tasks.Count;

    public string CountText => Count.ToString(CultureInfo.InvariantCulture);

    public string ArrowGlyph => IsExpanded ? "▾" : "▸";

    partial void OnIsExpandedChanged(bool value) => OnPropertyChanged(nameof(ArrowGlyph));

    [RelayCommand]
    private void Toggle() => _toggle(this);
}

/// <summary>任务行。命令直接挂在行上，XAML 模板里不用再绕回父级 DataContext。</summary>
public sealed partial class TaskRowViewModel : ObservableObject
{
    private readonly Action<TaskRowViewModel> _complete;
    private readonly Action<TaskRowViewModel> _edit;
    private readonly Action<TaskRowViewModel> _delete;

    public TaskRowViewModel(
        TaskItem task,
        string? projectName,
        DateTime now,
        Action<TaskRowViewModel> complete,
        Action<TaskRowViewModel> edit,
        Action<TaskRowViewModel> delete)
    {
        Task = task;
        ProjectName = projectName;
        _complete = complete;
        _edit = edit;
        _delete = delete;

        Group = task.Status is TaskStatus.Done or TaskStatus.Canceled
            ? Core.TaskGroup.Someday
            : TaskGrouping.Group(task, now);
        IsOverdue = task.Status is not (TaskStatus.Done or TaskStatus.Canceled)
                    && Formatting.IsOverdue(task.DueAt, LocalTime.ToLocalDateTime(now));
    }

    public TaskItem Task { get; }

    public string? ProjectName { get; }

    public Core.TaskGroup Group { get; }

    public bool IsFinished => Task.Status is TaskStatus.Done or TaskStatus.Canceled;

    public bool IsCanceled => Task.Status == TaskStatus.Canceled;

    public bool IsOverdue { get; }

    public string Title => Task.Title;

    public string DueText => Formatting.FormatLocalDateTime(Task.DueAt);

    public bool HasDue => !string.IsNullOrEmpty(Task.DueAt);

    /// <summary>逾期时只显示红色那一份，避免同一行出现两个相同时间。</summary>
    public bool ShowDueNormal => HasDue && !IsOverdue;

    public string StatusText => Task.Status switch
    {
        TaskStatus.Todo => "待办",
        TaskStatus.Doing => "进行中",
        TaskStatus.Blocked => "已阻塞",
        TaskStatus.Done => "已完成",
        _ => "已取消"
    };

    public string CheckGlyph => Task.Status switch
    {
        TaskStatus.Done => "✓",
        TaskStatus.Canceled => "×",
        _ => string.Empty
    };

    public string PriorityMark => Task.Priority switch
    {
        Priority.High => "高",
        Priority.Medium => "中",
        Priority.Low => "低",
        _ => string.Empty
    };

    public bool HasPriority => Task.Priority != Priority.None;

    public string EstimateText => Task.Estimate is null ? string.Empty : $"预估 {Formatting.FormatHours(Task.Estimate)}";

    public bool HasEstimate => Task.Estimate is not null;

    public bool HasProject => ProjectName is not null;

    public IBrush? PriorityBrush => Task.Priority switch
    {
        Priority.High => Brush.Parse("#ef6b6b"),
        Priority.Medium => Brush.Parse("#e0a04a"),
        _ => null
    };

    [RelayCommand]
    private void Complete() => _complete(this);

    [RelayCommand]
    private void Edit() => _edit(this);

    [RelayCommand]
    private void Delete() => _delete(this);
}

/// <summary>新建/编辑任务表单。</summary>
public partial class TaskFormViewModel : ObservableObject
{
    private readonly Action _save;
    private readonly Action _cancel;

    public static readonly IReadOnlyList<string> StatusOptions = ["待办", "进行中", "已阻塞", "已完成", "已取消"];

    public static readonly IReadOnlyList<string> PriorityOptions = ["无", "低", "中", "高"];

    [ObservableProperty]
    private string _title = string.Empty;

    [ObservableProperty]
    private string _notes = string.Empty;

    [ObservableProperty]
    private int _projectIndex;

    [ObservableProperty]
    private int _statusIndex;

    [ObservableProperty]
    private int _priorityIndex;

    [ObservableProperty]
    private string _dueAtText = string.Empty;

    [ObservableProperty]
    private string _remindBefore = "0";

    [ObservableProperty]
    private string _estimate = string.Empty;

    [ObservableProperty]
    private string _spent = string.Empty;

    [ObservableProperty]
    private string _blockedReason = string.Empty;

    [ObservableProperty]
    private string? _error;

    public string? TaskId { get; private init; }

    public bool IsEditing => TaskId is not null;

    public string TitleText => IsEditing ? "编辑任务" : "新建任务";

    public IReadOnlyList<ProjectOption> ProjectChoices { get; private set; } = [ProjectOption.Standalone];

    public bool IsBlocked => StatusIndex == 2;

    partial void OnStatusIndexChanged(int value) => OnPropertyChanged(nameof(IsBlocked));

    private TaskFormViewModel(Action save, Action cancel)
    {
        _save = save;
        _cancel = cancel;
    }

    [RelayCommand]
    private void Save() => _save();

    [RelayCommand]
    private void Cancel() => _cancel();

    public static TaskFormViewModel ForCreate(
        IReadOnlyList<ProjectOption> choices,
        Action save,
        Action cancel) =>
        new(save, cancel)
        {
            ProjectChoices = FormChoices(choices),
            ProjectIndex = 0
        };

    public static TaskFormViewModel ForEdit(
        TaskItem task,
        IReadOnlyList<ProjectOption> choices,
        Action save,
        Action cancel)
    {
        var options = FormChoices(choices);

        var projectIndex = 0;
        if (task.ProjectId is not null)
        {
            var found = options.FindIndex(o => o.Id == task.ProjectId);
            if (found >= 0) projectIndex = found;
        }

        return new TaskFormViewModel(save, cancel)
        {
            TaskId = task.Id,
            Title = task.Title,
            Notes = task.Notes,
            ProjectChoices = options,
            ProjectIndex = projectIndex,
            StatusIndex = task.Status switch
            {
                TaskStatus.Todo => 0,
                TaskStatus.Doing => 1,
                TaskStatus.Blocked => 2,
                TaskStatus.Done => 3,
                _ => 4
            },
            PriorityIndex = task.Priority switch
            {
                Priority.None => 0,
                Priority.Low => 1,
                Priority.Medium => 2,
                _ => 3
            },
            DueAtText = task.DueAt is null ? string.Empty : task.DueAt.Replace('T', ' '),
            RemindBefore = task.RemindBefore.ToString(CultureInfo.InvariantCulture),
            Estimate = task.Estimate?.ToString("0.##", CultureInfo.InvariantCulture) ?? string.Empty,
            Spent = task.Spent?.ToString("0.##", CultureInfo.InvariantCulture) ?? string.Empty,
            BlockedReason = task.BlockedReason ?? string.Empty
        };
    }

    public bool Validate()
    {
        if (string.IsNullOrWhiteSpace(Title))
        {
            Error = "任务标题不能为空";
            return false;
        }

        if (!TryParseDueAt(DueAtText, out _, out var dueError))
        {
            Error = dueError;
            return false;
        }

        if (!IsOptionalNonNegativeInt(RemindBefore))
        {
            Error = "提前提醒应为非负整数（分钟）";
            return false;
        }

        if (!IsOptionalHours(Estimate) || !IsOptionalHours(Spent))
        {
            Error = "预估/实际工时应为非负数";
            return false;
        }

        Error = null;
        return true;
    }

    public CreateTaskInput BuildCreate()
    {
        TryParseDueAt(DueAtText, out var dueAt, out _);
        return new CreateTaskInput
        {
            Title = Title.Trim(),
            Notes = Notes.Trim(),
            ProjectId = SelectedProjectId,
            Status = ToStatus(StatusIndex),
            Priority = ToPriority(PriorityIndex),
            DueAt = dueAt,
            RemindBefore = ParseNullableInt(RemindBefore) ?? 0,
            Estimate = ParseNullableDouble(Estimate),
            Spent = ParseNullableDouble(Spent),
            BlockedReason = StatusIndex == 2 ? NullIfEmpty(BlockedReason) : null
        };
    }

    public UpdateTaskInput BuildUpdate()
    {
        TryParseDueAt(DueAtText, out var dueAt, out _);
        return new UpdateTaskInput
        {
            Title = Optional<string>.Some(Title.Trim()),
            Notes = Optional<string?>.Some(Notes.Trim()),
            ProjectId = Optional<string?>.Some(SelectedProjectId),
            Status = Optional<TaskStatus>.Some(ToStatus(StatusIndex)),
            Priority = Optional<Priority>.Some(ToPriority(PriorityIndex)),
            DueAt = Optional<string?>.Some(dueAt),
            RemindBefore = Optional<int?>.Some(ParseNullableInt(RemindBefore) ?? 0),
            Estimate = Optional<double?>.Some(ParseNullableDouble(Estimate)),
            Spent = Optional<double?>.Some(ParseNullableDouble(Spent)),
            BlockedReason = Optional<string?>.Some(StatusIndex == 2 ? NullIfEmpty(BlockedReason) : null)
        };
    }

    private string? SelectedProjectId =>
        ProjectIndex >= 0 && ProjectIndex < ProjectChoices.Count ? ProjectChoices[ProjectIndex].Id : null;

    /// <summary>
    /// 表单里的项目选项 = 「独立任务」+ 未归档的真实项目。
    /// 筛选下拉传进来的「全部项目」与已归档项目都不是可保存的值，这里必须过滤掉。
    /// </summary>
    private static List<ProjectOption> FormChoices(IReadOnlyList<ProjectOption> choices)
    {
        var options = new List<ProjectOption> { ProjectOption.Standalone };
        options.AddRange(choices.Where(o => o.Id is not null && !o.IsArchived));
        return options;
    }

    /// <summary>
    /// 允许三种写法：空、日期、日期+时间。只给日期时补 18:00（下班前默认时刻），
    /// 并在界面上写清楚，避免隐式规则让人猜。
    /// </summary>
    private static bool TryParseDueAt(string text, out string? dueAt, out string? error)
    {
        dueAt = null;
        error = null;

        var value = text.Trim();
        if (value.Length == 0) return true;

        string[] formats = ["yyyy-MM-dd HH:mm", "yyyy-MM-ddTHH:mm", "yyyy-MM-dd HH:mm:ss", "yyyy-MM-dd"];
        if (!DateTime.TryParseExact(value, formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
        {
            error = "截止时间格式应为 YYYY-MM-DD HH:mm（也可只填日期，默认 18:00）";
            return false;
        }

        if (value.Length == 10)
        {
            parsed = parsed.Date.AddHours(18);
        }

        dueAt = LocalTime.ToLocalDateTime(parsed);
        return true;
    }

    private static bool IsOptionalNonNegativeInt(string value) =>
        string.IsNullOrWhiteSpace(value)
        || int.TryParse(value.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) && parsed >= 0;

    private static bool IsOptionalHours(string value) =>
        string.IsNullOrWhiteSpace(value)
        || double.TryParse(value.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) && parsed >= 0;

    private static int? ParseNullableInt(string value) =>
        int.TryParse(value.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) ? parsed : null;

    private static double? ParseNullableDouble(string value) =>
        double.TryParse(value.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) ? parsed : null;

    private static string? NullIfEmpty(string value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static TaskStatus ToStatus(int index) => index switch
    {
        1 => TaskStatus.Doing,
        2 => TaskStatus.Blocked,
        3 => TaskStatus.Done,
        4 => TaskStatus.Canceled,
        _ => TaskStatus.Todo
    };

    private static Priority ToPriority(int index) => index switch
    {
        1 => Priority.Low,
        2 => Priority.Medium,
        3 => Priority.High,
        _ => Priority.None
    };
}
