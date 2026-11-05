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

    private bool _isReloading;
    private bool _reloadPending;
    private bool _isRebuildingOptions;

    public TasksViewModel(WorkStationService service, Action<string, bool> toast, Func<DateTime>? clock = null)
    {
        _service = service;
        _toast = toast;
        _clock = clock ?? (() => DateTime.Now);

        StatusFilters =
        [
            new StatusFilter("all", "全部", null, f => SelectedStatusFilter = f),
            new StatusFilter("todo", "待办", TaskStatus.Todo, f => SelectedStatusFilter = f),
            new StatusFilter("doing", "进行中", TaskStatus.Doing, f => SelectedStatusFilter = f),
            new StatusFilter("blocked", "已阻塞", TaskStatus.Blocked, f => SelectedStatusFilter = f),
            new StatusFilter("done", "已完成", TaskStatus.Done, f => SelectedStatusFilter = f),
            new StatusFilter("canceled", "已取消", TaskStatus.Canceled, f => SelectedStatusFilter = f)
        ];
        _selectedStatusFilter = StatusFilters[0];
        StatusFilters[0].IsSelected = true;
    }

    public ObservableCollection<TaskGroupViewModel> Groups { get; } = [];

    public ObservableCollection<ProjectOption> ProjectOptions { get; } = [];

    public IReadOnlyList<StatusFilter> StatusFilters { get; }

    [ObservableProperty]
    private StatusFilter _selectedStatusFilter;

    [ObservableProperty]
    private ProjectOption? _selectedProject;

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

    partial void OnSelectedStatusFilterChanged(StatusFilter value)
    {
        foreach (var filter in StatusFilters)
        {
            filter.IsSelected = ReferenceEquals(filter, value);
        }

        Reload();
    }

    partial void OnSelectedProjectChanged(ProjectOption? value)
    {
        if (_isRebuildingOptions) return;
        Reload();
    }

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
        // 重入保护：重建项目下拉会让 ComboBox 把 SelectedItem 写回 null，
        // 从而再触发一次 Reload（见 ReloadProjectOptions）。允许它无限套娃、
        // 或者让内外两层同时往里 Add，都会得到重复项。这里把嵌套请求合并成一次补跑。
        if (_isReloading)
        {
            _reloadPending = true;
            return;
        }

        _isReloading = true;
        try
        {
            do
            {
                _reloadPending = false;
                ReloadCore();
            }
            while (_reloadPending);
        }
        finally
        {
            _isReloading = false;
        }
    }

    private void ReloadCore()
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

        var desired = new List<ProjectOption> { ProjectOption.All };
        foreach (var project in projects.DistinctBy(p => p.Id))
        {
            var archived = project.ArchivedAt is not null;
            desired.Add(new ProjectOption(
                project.Id,
                archived ? $"{project.Name}（已归档）" : project.Name,
                archived));
        }

        // 原地同步而不是 Clear()+重建：清空会让 ComboBox 把 SelectedItem 先置空，
        // 之后补回的任何实例它都不再认，折叠框就一直是空白。
        // 重建期间仍要吞掉「选中项被删掉」引发的回写，否则会多跑一轮完整的 Reload。
        _isRebuildingOptions = true;
        try
        {
            ProjectOptionList.Sync(ProjectOptions, desired);
        }
        finally
        {
            _isRebuildingOptions = false;
        }

        // 选中项还在就保持同一个实例（引用相同直接跳过），不触发任何绑定回写；
        // 找不到（项目被归档/删除）才回落到「全部项目」。
        var next = ProjectOptions.FirstOrDefault(o => o.Id == previous) ?? ProjectOptions[0];
        if (!ReferenceEquals(SelectedProject, next)) SelectedProject = next;
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

    /// <summary>
    /// 从看板或命令面板跳进来的编辑入口。任务可能不在当前筛选结果里，
    /// 所以不能从 Groups 里找行，直接按实体构造表单。
    /// </summary>
    public void OpenEditor(TaskItem task)
    {
        Form = TaskFormViewModel.ForEdit(task, ProjectOptions, SaveForm, CloseForm);
        IsFormOpen = true;
    }

    /// <summary>按项目筛选。项目不在下拉里（已归档等）时静默忽略，避免把筛选重置成「全部」。</summary>
    public void FocusProject(string projectId)
    {
        var option = ProjectOptions.FirstOrDefault(o => o.Id == projectId);
        if (option is null) return;

        SelectedProject = option;
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

/// <summary>
/// 状态筛选项。做成对象而不是 record：胶囊要直接绑「选中态」和「点击命令」，
/// 否则得在 XAML 里塞比较转换器，还得再绕回父 DataContext 才能发命令。
/// </summary>
public sealed partial class StatusFilter : ObservableObject
{
    public StatusFilter(string key, string label, TaskStatus? status, Action<StatusFilter> select)
    {
        Key = key;
        Label = label;
        Status = status;
        SelectCommand = new RelayCommand(() => select(this));
    }

    public string Key { get; }

    public string Label { get; }

    public TaskStatus? Status { get; }

    public IRelayCommand SelectCommand { get; }

    [ObservableProperty]
    private bool _isSelected;
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

    /// <summary>逾期组用告警色标题；其余组保持中性，避免整页都在喊。</summary>
    public bool IsUrgent => Key == "overdue";

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

    /// <summary>优先级色条。中间调，浅/深两套主题下都能分辨。</summary>
    public IBrush PriorityBrush => Task.Priority switch
    {
        Priority.High => Brush.Parse("#D9534F"),
        Priority.Medium => Brush.Parse("#D9932B"),
        Priority.Low => Brush.Parse("#8A94A6"),
        _ => Brushes.Transparent
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

    /// <summary>
    /// 行内第二行文字。合成一个属性而不是在 XAML 里拼三段 + 分隔符：
    /// 分隔符的显隐条件互相牵连，摆在视图里必然出错，而且会留下孤儿「·」。
    /// </summary>
    public string MetaText
    {
        get
        {
            var parts = new List<string>(3);
            if (ProjectName is not null) parts.Add(ProjectName);

            var priority = PriorityMark switch
            {
                "高" => "高优先级",
                "中" => "中优先级",
                "低" => "低优先级",
                _ => null
            };
            if (priority is not null) parts.Add(priority);

            if (IsFinished) parts.Add(StatusText);

            return string.Join(" · ", parts);
        }
    }

    public bool HasMeta => MetaText.Length > 0;

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

    /// <summary>截止日期。控件是 CalendarDatePicker，null 表示不设截止时间。</summary>
    [ObservableProperty]
    private DateTime? _dueDate;

    /// <summary>截止时刻。控件是 TimePicker；只选日期时默认落到 18:00。</summary>
    [ObservableProperty]
    private TimeSpan? _dueTime;

    public bool HasDueDate => DueDate is not null;

    /// <summary>提前提醒的预设项。0 表示不提醒（与库里 remind_before = 0 语义一致）。</summary>
    public static IReadOnlyList<ReminderLeadOption> LeadPresets { get; } =
    [
        new(0, "不提醒"),
        new(15, "提前 15 分钟"),
        new(60, "提前 1 小时"),
        new(180, "提前 3 小时"),
        new(1440, "提前 1 天")
    ];

    /// <summary>下拉项。库里存的历史值不在预设里时，会补一条「提前 N 分钟」。</summary>
    public ObservableCollection<ReminderLeadOption> LeadOptions { get; } = [];

    [ObservableProperty]
    private ReminderLeadOption? _selectedLead;

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

    partial void OnDueDateChanged(DateTime? value)
    {
        // 只挑日期时补一个下班前的默认时刻，并直接写进时间选择器，
        // 让默认值可见可改，而不是藏在保存逻辑里。
        if (value is not null && DueTime is null)
        {
            DueTime = DefaultDueTime;
        }

        OnPropertyChanged(nameof(HasDueDate));
    }

    private static readonly TimeSpan DefaultDueTime = new(18, 0, 0);

    [RelayCommand]
    private void ClearDue()
    {
        DueDate = null;
        DueTime = null;
    }

    private TaskFormViewModel(Action save, Action cancel)
    {
        _save = save;
        _cancel = cancel;

        foreach (var preset in LeadPresets) LeadOptions.Add(preset);
        _selectedLead = LeadOptions[0];
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

        var hasDue = TryParseStoredDueAt(task.DueAt, out var due);

        var form = new TaskFormViewModel(save, cancel)
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
            DueDate = hasDue ? due.Date : null,
            DueTime = hasDue ? due.TimeOfDay : null,
            Estimate = task.Estimate?.ToString("0.##", CultureInfo.InvariantCulture) ?? string.Empty,
            Spent = task.Spent?.ToString("0.##", CultureInfo.InvariantCulture) ?? string.Empty,
            BlockedReason = task.BlockedReason ?? string.Empty
        };

        // 下拉项依赖实例上的集合，只能在对象建好之后再认领（历史值不在预设里会补一条）
        form.SelectedLead = form.FindLeadOption(task.RemindBefore);
        return form;
    }

    public bool Validate()
    {
        if (string.IsNullOrWhiteSpace(Title))
        {
            Error = "任务标题不能为空";
            return false;
        }

        if (DueDate is null && DueTime is not null)
        {
            Error = "选了时刻还要选日期，或者用「清除」去掉截止时间";
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
        return new CreateTaskInput
        {
            Title = Title.Trim(),
            Notes = Notes.Trim(),
            ProjectId = SelectedProjectId,
            Status = ToStatus(StatusIndex),
            Priority = ToPriority(PriorityIndex),
            DueAt = BuildDueAt(),
            RemindBefore = SelectedLead?.Minutes ?? 0,
            Estimate = ParseNullableDouble(Estimate),
            Spent = ParseNullableDouble(Spent),
            BlockedReason = StatusIndex == 2 ? NullIfEmpty(BlockedReason) : null
        };
    }

    public UpdateTaskInput BuildUpdate()
    {
        return new UpdateTaskInput
        {
            Title = Optional<string>.Some(Title.Trim()),
            Notes = Optional<string?>.Some(Notes.Trim()),
            ProjectId = Optional<string?>.Some(SelectedProjectId),
            Status = Optional<TaskStatus>.Some(ToStatus(StatusIndex)),
            Priority = Optional<Priority>.Some(ToPriority(PriorityIndex)),
            DueAt = Optional<string?>.Some(BuildDueAt()),
            RemindBefore = Optional<int?>.Some(SelectedLead?.Minutes ?? 0),
            Estimate = Optional<double?>.Some(ParseNullableDouble(Estimate)),
            Spent = Optional<double?>.Some(ParseNullableDouble(Spent)),
            BlockedReason = Optional<string?>.Some(StatusIndex == 2 ? NullIfEmpty(BlockedReason) : null)
        };
    }

    private string? SelectedProjectId =>
        ProjectIndex >= 0 && ProjectIndex < ProjectChoices.Count ? ProjectChoices[ProjectIndex].Id : null;

    /// <summary>
    /// 表单里的项目选项 = 「独立任务」+ 未归档的真实项目。
    /// 筛选下拉传进来的「全部项目」与已归档项目都不是可保存的值，这里必须过滤掉；
    /// 同时按 Id 去重，避免上游列表万一带上重复项时一路传到表单里。
    /// </summary>
    private static List<ProjectOption> FormChoices(IReadOnlyList<ProjectOption> choices)
    {
        var options = new List<ProjectOption> { ProjectOption.Standalone };
        options.AddRange(choices
            .Where(o => o.Id is not null && !o.IsArchived)
            .DistinctBy(o => o.Id));
        return options;
    }

    /// <summary>
    /// 从库里存的本地时间串还原出日期/时刻。库里只有一种格式（yyyy-MM-ddTHH:mm），
    /// 其余写法是历史数据兜底，解析不了就当没设截止时间。
    /// </summary>
    private static bool TryParseStoredDueAt(string? value, out DateTime parsed)
    {
        parsed = default;
        if (string.IsNullOrWhiteSpace(value)) return false;

        string[] formats = ["yyyy-MM-ddTHH:mm", "yyyy-MM-ddTHH:mm:ss", "yyyy-MM-dd HH:mm", "yyyy-MM-dd"];
        return DateTime.TryParseExact(value.Trim(), formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out parsed);
    }

    /// <summary>日期选择器 + 时间选择器合成库里要的 "yyyy-MM-ddTHH:mm"；只留时刻不给日期视为未设置。</summary>
    private string? BuildDueAt()
    {
        if (DueDate is not { } date) return null;

        var time = DueTime ?? DefaultDueTime;
        return LocalTime.ToLocalDateTime(date.Date.Add(time));
    }

    private static bool IsOptionalHours(string value) =>
        string.IsNullOrWhiteSpace(value)
        || double.TryParse(value.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) && parsed >= 0;

    private static int? ParseNullableInt(string value) =>
        int.TryParse(value.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) ? parsed : null;

    private static double? ParseNullableDouble(string value) =>
        double.TryParse(value.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) ? parsed : null;

    /// <summary>按分钟数找回下拉项；库里存的值不在预设里就补一条，避免折叠框空白。</summary>
    private ReminderLeadOption FindLeadOption(int minutes)
    {
        var found = LeadOptions.FirstOrDefault(o => o.Minutes == minutes);
        if (found is not null) return found;

        var custom = new ReminderLeadOption(minutes, ReminderLeadOption.Describe(minutes));
        LeadOptions.Add(custom);
        return custom;
    }

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

/// <summary>提前提醒的一个选项。文案自己算，XAML 里只绑定 Label。</summary>
public sealed record ReminderLeadOption(int Minutes, string Label)
{
    public static string Describe(int minutes) => minutes switch
    {
        <= 0 => "不提醒",
        < 60 => $"提前 {minutes} 分钟",
        _ when minutes % 1440 == 0 => $"提前 {minutes / 1440} 天",
        _ when minutes % 60 == 0 => $"提前 {minutes / 60} 小时",
        _ => $"提前 {minutes} 分钟"
    };
}
