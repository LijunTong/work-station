using System.Collections.ObjectModel;
using System.Globalization;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WorkStation.Core;
using WorkStation.Core.Board;
using WorkStation.Core.Models;
using WorkStation.Core.Projects;
using WorkStation.Core.Services;
using WorkStation.Core.Time;

namespace WorkStation.App.ViewModels;

public enum BoardMode
{
    Tasks,
    Projects
}

/// <summary>
/// 看板页：任务看板 + 项目看板，拖拽换列即改状态。
///
/// 视图层只负责「把拖拽的落点翻译成一次 MoveTask / MoveProject 调用」，
/// 状态迁移与提示全在这里，这样无头测试可以覆盖到真正的行为，
/// 而不需要模拟一套桌面拖拽。
/// </summary>
public partial class BoardViewModel : ObservableObject
{
    private readonly WorkStationService _service;
    private readonly Action<string, bool> _toast;
    private readonly Func<DateTime> _clock;
    private readonly Action? _dataChanged;

    private bool _isReloading;
    private bool _reloadPending;
    private bool _isRebuildingOptions;
    private bool _suppressReload;

    public BoardViewModel(
        WorkStationService service,
        Action<string, bool> toast,
        Func<DateTime>? clock = null,
        Action? dataChanged = null)
    {
        _service = service;
        _toast = toast;
        _clock = clock ?? (() => DateTime.Now);
        _dataChanged = dataChanged;

        // 口径顺序：今日 / 本周 / 本月 / 逾期。
        // 逾期排在最后：它是「异常集合」而不是一个时间段，和三个时间口径并列时会读成同类。
        OverviewChips =
        [
            new BoardChip("today", "今日", SelectOverview, showCount: true),
            new BoardChip("week", "本周", SelectOverview, showCount: true),
            new BoardChip("month", "本月", SelectOverview, showCount: true),
            new BoardChip("overdue", "逾期", SelectOverview, showCount: true)
        ];

        PriorityChips =
        [
            new BoardChip("all", "全部优先级", SelectPriority),
            new BoardChip("high", "高", SelectPriority),
            new BoardChip("medium", "中", SelectPriority),
            new BoardChip("low", "低", SelectPriority)
        ];

        _selectedPriority = PriorityChips[0];
        PriorityChips[0].IsSelected = true;
        SyncOverviewChips();
    }

    public ObservableCollection<BoardColumnViewModel> Columns { get; } = [];

    public ObservableCollection<BoardProjectColumnViewModel> ProjectColumns { get; } = [];

    public ObservableCollection<ProjectOption> ProjectOptions { get; } = [];

    public IReadOnlyList<BoardChip> OverviewChips { get; }

    public IReadOnlyList<BoardChip> PriorityChips { get; }

    /// <summary>点击卡片或拖拽之外的入口：打开任务编辑抽屉。</summary>
    public Action<TaskItem>? EditTaskRequested { get; set; }

    /// <summary>项目卡片上的「进入任务看板」：带项目筛选跳到任务页。</summary>
    public Action<string>? OpenProjectTasksRequested { get; set; }

    [ObservableProperty]
    private bool _isTaskBoard = true;

    [ObservableProperty]
    private ProjectOption? _selectedProject;

    [ObservableProperty]
    private BoardChip _selectedPriority;

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private bool _showAllDone;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string? _error;

    /// <summary>当前时间筛选口径。由概览条上的今日/逾期/本周胶囊驱动。</summary>
    public BoardTimeScope TimeScope { get; private set; } = BoardTimeScope.All;

    /// <summary>概览条的「已阻塞」是一个独立开关，与时间口径互不干扰。</summary>
    public bool OnlyBlocked { get; private set; }

    public bool IsProjectBoard => !IsTaskBoard;

    public bool HasError => Error is not null;

    public bool HasFilter =>
        TimeScope != BoardTimeScope.All
        || OnlyBlocked
        || SelectedPriority is { Key: not "all" }
        || SelectedProject is { Id: not null }
        || !string.IsNullOrWhiteSpace(SearchText);

    public string DoneWindowLabel => ShowAllDone ? "只看近 7 天" : "显示全部";

    /// <summary>
    /// 逾期任务数（未完成且已到点）。
    ///
    /// 逾期是**时间属性**、不是工作流状态，所以看板不给它单开一列：
    /// 同一张卡会同时出现在「待办」和「已逾期」里，数量对不上，
    /// 「拖进逾期列」也定义不出动作。逾期做成**视图入口**更合适 ——
    /// 顶部一条提示，点一下切到逾期口径。
    /// </summary>
    public int OverdueCount { get; private set; }

    /// <summary>最早那条逾期的日期（MM-dd）。</summary>
    public string EarliestOverdueText { get; private set; } = string.Empty;

    /// <summary>顶部提示条文案。</summary>
    public string OverdueHintText => OverdueCount > 0
        ? $"{OverdueCount} 条已逾期，最早 {EarliestOverdueText}"
        : string.Empty;

    /// <summary>有逾期任务、且当前不是「只看逾期」口径时才提示（已经在看逾期就别再催）。</summary>
    public bool ShowOverdueHint => OverdueCount > 0 && TimeScope != BoardTimeScope.Overdue;

    /// <summary>顶部提示条：切到「只看逾期」。</summary>
    [RelayCommand]
    private void ShowOverdueOnly() => ShowScope(BoardTimeScope.Overdue);

    /// <summary>统计逾期任务与最早逾期日期，供顶部提示条使用。</summary>
    private void UpdateOverdueHint(IReadOnlyList<TaskItem> tasks, DateTime now)
    {
        var overdue = tasks
            .Where(TaskBoard.IsOpen)
            .Where(t => TaskGrouping.Group(t, now) == TaskGroup.Overdue)
            .ToList();

        OverdueCount = overdue.Count;

        var earliest = overdue
            .Select(t => t.DueAt)
            .Where(d => !string.IsNullOrEmpty(d))
            .OrderBy(d => d, StringComparer.Ordinal)
            .FirstOrDefault();

        EarliestOverdueText = earliest is { Length: >= 10 } ? earliest.Substring(5, 5) : string.Empty;

        OnPropertyChanged(nameof(OverdueCount));
        OnPropertyChanged(nameof(EarliestOverdueText));
        OnPropertyChanged(nameof(OverdueHintText));
        OnPropertyChanged(nameof(ShowOverdueHint));
    }

    partial void OnIsTaskBoardChanged(bool value) => OnPropertyChanged(nameof(IsProjectBoard));

    partial void OnSelectedProjectChanged(ProjectOption? value)
    {
        if (_isRebuildingOptions) return;
        ReloadIfNeeded();
    }

    partial void OnSelectedPriorityChanged(BoardChip value)
    {
        foreach (var chip in PriorityChips)
        {
            chip.IsSelected = ReferenceEquals(chip, value);
        }

        ReloadIfNeeded();
    }

    partial void OnSearchTextChanged(string value) => ReloadIfNeeded();

    partial void OnShowAllDoneChanged(bool value)
    {
        OnPropertyChanged(nameof(DoneWindowLabel));
        ReloadIfNeeded();
    }

    partial void OnErrorChanged(string? value) => OnPropertyChanged(nameof(HasError));

    private void ReloadIfNeeded()
    {
        if (_suppressReload) return;
        OnPropertyChanged(nameof(HasFilter));
        Reload();
    }

    public void Reload()
    {
        // 与任务页同源的重入保护：重建项目下拉会让 ComboBox 回写选中项，从而再次触发 Reload
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
            var tasksResult = _service.TasksList();

            if (!tasksResult.Ok || tasksResult.Data is null)
            {
                Error = tasksResult.Error?.Message ?? "任务加载失败";
                Columns.Clear();
                ProjectColumns.Clear();
                return;
            }

            var projects = projectsResult.Data ?? [];
            var tasks = tasksResult.Data;
            var now = _clock();

            var overview = TaskBoard.ComputeOverview(tasks, now);
            UpdateOverdueHint(tasks, now);
            SetChipCount("today", overview.Today);
            SetChipCount("overdue", overview.Overdue);
            SetChipCount("week", overview.Week);
            SetChipCount("month", overview.Month);

            RebuildProjectOptions(projects);

            var visible = tasks
                .Where(t => t.Status != TaskStatus.Canceled)
                .Where(t => MatchesFilters(t))
                .ToList();

            BuildTaskColumns(visible, projects, now);
            BuildProjectColumns(projects, tasks, now);

            Error = projectsResult.Ok
                ? null
                : "项目列表加载失败，卡片上的项目名可能显示为「未知项目」";
        }
        finally
        {
            IsLoading = false;
            OnPropertyChanged(nameof(HasFilter));
        }
    }

    private bool MatchesFilters(TaskItem task)
    {
        if (SelectedProject is { Id: not null } project && task.ProjectId != project.Id) return false;

        var priority = SelectedPriority.Key switch
        {
            "high" => Priority.High,
            "medium" => Priority.Medium,
            "low" => Priority.Low,
            _ => (Priority?)null
        };
        if (priority is not null && task.Priority != priority) return false;

        if (OnlyBlocked && task.Status != TaskStatus.Blocked) return false;

        if (!TaskBoard.MatchesTimeScope(task, TimeScope, _clock())) return false;

        if (!string.IsNullOrWhiteSpace(SearchText))
        {
            var keyword = SearchText.Trim();
            var hit = task.Title.Contains(keyword, StringComparison.OrdinalIgnoreCase)
                      || task.Notes.Contains(keyword, StringComparison.OrdinalIgnoreCase);
            if (!hit) return false;
        }

        return true;
    }

    private void BuildTaskColumns(IReadOnlyList<TaskItem> tasks, IReadOnlyList<Project> projects, DateTime now)
    {
        var projectById = projects
            .GroupBy(p => p.Id)
            .ToDictionary(g => g.Key, g => g.First());

        Columns.Clear();
        foreach (var status in TaskBoard.ColumnOrder)
        {
            var matching = tasks.Where(t => t.Status == status);

            // 「已完成」列默认只留近 7 天；其余列没有时间窗口
            if (status == TaskStatus.Done && !ShowAllDone)
            {
                matching = matching.Where(t => TaskBoard.IsInDoneWindow(t, now));
            }

            var cards = (status == TaskStatus.Done
                    ? matching.OrderBy(t => t, TaskOrdering.CompletedTaskComparer())
                    : // 未完成：优先级在前，同优先级再按逾期、截止时间
                    matching.OrderBy(t => t, TaskOrdering.OpenTaskComparer(now)))
                .Select(t =>
                {
                    projectById.TryGetValue(t.ProjectId ?? string.Empty, out var project);
                    return new BoardTaskCardViewModel(t, project?.Name, project?.Color, now);
                })
                .ToList();

            Columns.Add(new BoardColumnViewModel(status, cards, ToggleDoneWindow, ShowAllDone));
        }
    }

    private void BuildProjectColumns(IReadOnlyList<Project> projects, IReadOnlyList<TaskItem> tasks, DateTime now)
    {
        var tasksByProject = tasks
            .Where(t => t.ProjectId is not null)
            .GroupBy(t => t.ProjectId!)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<TaskItem>)g.ToList());

        ProjectColumns.Clear();
        foreach (var status in BoardProjectColumnViewModel.StatusOrder)
        {
            var cards = projects
                .Where(p => p.ArchivedAt is null && p.Status == status)
                // 优先级高的项目排前面。用显式权重：Priority 的枚举值是高=0、无=3，
                // 直接 OrderByDescending 会把「无优先级」顶到最前。
                .OrderBy(p => TaskOrdering.PriorityWeight(p.Priority))
                .ThenBy(p => p.SortOrder)
                .ThenBy(p => p.CreatedAt, StringComparer.Ordinal)
                .Select(p =>
                {
                    tasksByProject.TryGetValue(p.Id, out var projectTasks);
                    return new BoardProjectCardViewModel(p, projectTasks ?? [], now, OpenProjectTasks);
                })
                .ToList();

            ProjectColumns.Add(new BoardProjectColumnViewModel(status, cards));
        }
    }

    private void RebuildProjectOptions(IReadOnlyList<Project> projects)
    {
        var previous = SelectedProject?.Id;

        var desired = new List<ProjectOption> { ProjectOption.All };
        foreach (var project in projects.Where(p => p.ArchivedAt is null).DistinctBy(p => p.Id))
        {
            desired.Add(new ProjectOption(project.Id, project.Name));
        }

        // 原地同步而不是 Clear()+重建：清空会让 ComboBox 把 SelectedItem 先置空，
        // 之后补回的实例它不再认，折叠框就一直是空白（「选了项目却看不到」）。
        _isRebuildingOptions = true;
        try
        {
            ProjectOptionList.Sync(ProjectOptions, desired);
        }
        finally
        {
            _isRebuildingOptions = false;
        }

        // 选中项还在就保持同一个实例，不触发绑定回写
        var next = ProjectOptions.FirstOrDefault(o => o.Id == previous) ?? ProjectOptions[0];
        if (!ReferenceEquals(SelectedProject, next)) SelectedProject = next;
    }

    private void SelectPriority(BoardChip chip)
    {
        // ToggleButton 点击会先翻转 IsSelected：单选语义下不允许取消唯一选中项，点击当前项直接复位
        chip.IsSelected = true;
        if (!ReferenceEquals(SelectedPriority, chip)) SelectedPriority = chip;
    }

    private void SelectOverview(BoardChip chip)
    {
        switch (chip.Key)
        {
            case "today":
                TimeScope = TimeScope == BoardTimeScope.Today ? BoardTimeScope.All : BoardTimeScope.Today;
                break;
            case "overdue":
                TimeScope = TimeScope == BoardTimeScope.Overdue ? BoardTimeScope.All : BoardTimeScope.Overdue;
                break;
            case "week":
                TimeScope = TimeScope == BoardTimeScope.Week ? BoardTimeScope.All : BoardTimeScope.Week;
                break;
            case "month":
                TimeScope = TimeScope == BoardTimeScope.Month ? BoardTimeScope.All : BoardTimeScope.Month;
                break;
        }

        SyncOverviewChips();
        OnPropertyChanged(nameof(ShowOverdueHint));
        ReloadIfNeeded();
    }

    private void SyncOverviewChips()
    {
        foreach (var chip in OverviewChips)
        {
            chip.IsSelected = chip.Key switch
            {
                "today" => TimeScope == BoardTimeScope.Today,
                "overdue" => TimeScope == BoardTimeScope.Overdue,
                "week" => TimeScope == BoardTimeScope.Week,
                "month" => TimeScope == BoardTimeScope.Month,
                _ => false
            };
        }
    }

    /// <summary>按 key 写概览条上的数字。用 key 而不是下标：增删胶囊时下标会整体错位。</summary>
    private void SetChipCount(string key, int count)
    {
        foreach (var chip in OverviewChips)
        {
            if (chip.Key == key)
            {
                chip.Count = count;
                return;
            }
        }
    }

    [RelayCommand]
    private void SwitchBoard(string mode)
    {
        IsTaskBoard = !string.Equals(mode, nameof(BoardMode.Projects), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>切到「今日」口径并刷新。已经在今日时也重新拉一次数据。</summary>
    public void ShowToday()
    {
        TimeScope = BoardTimeScope.Today;
        SyncOverviewChips();
        Reload();
    }

    /// <summary>
    /// 外部入口（仪表盘跳转）：套用指定的时间口径并刷新。
    /// 「阻塞」不是时间口径，由仪表盘单独走 OnlyBlocked 通道。
    /// </summary>
    public void ShowScope(BoardTimeScope scope)
    {
        TimeScope = scope;
        SyncOverviewChips();
        OnPropertyChanged(nameof(ShowOverdueHint));
        Reload();
    }

    /// <summary>外部入口（仪表盘跳转）：只看已阻塞的任务。</summary>
    public void ShowBlockedOnly()
    {
        OnlyBlocked = true;
        TimeScope = BoardTimeScope.All;
        SyncOverviewChips();
        Reload();
    }

    [RelayCommand]
    private void ToggleDoneWindow() => ShowAllDone = !ShowAllDone;

    [RelayCommand]
    private void ClearFilters()
    {
        _suppressReload = true;
        try
        {
            SearchText = string.Empty;
            TimeScope = BoardTimeScope.All;
            OnlyBlocked = false;
            ShowAllDone = false;
            SelectedPriority = PriorityChips[0];
            SelectedProject = ProjectOptions.FirstOrDefault();
            SyncOverviewChips();
        }
        finally
        {
            _suppressReload = false;
        }

        Reload();
    }

    /// <summary>
    /// 拖拽落点：把任务移到目标列。返回是否真的落库 —— 视图层不用关心失败细节。
    /// </summary>
    public bool MoveTask(string taskId, TaskStatus target)
    {
        var current = _service.TasksGet(taskId);
        if (!current.Ok)
        {
            _toast(current.Error?.Message ?? "任务读取失败", true);
            Reload();
            return false;
        }

        if (current.Data is null)
        {
            // 卡片上的任务已经被别处删掉：刷新而不是静默失败，否则卡片会一直挂着
            _toast("任务已不存在，看板已刷新", true);
            Reload();
            return false;
        }

        if (current.Data.Status == target)
        {
            // 拖回原列：什么都不做，但仍然刷新一次，让视觉状态回到干净状态
            Reload();
            return false;
        }

        var result = _service.TasksSetStatus(taskId, target);
        if (!result.Ok)
        {
            _toast(result.Error?.Message ?? "更新状态失败", true);
            Reload();
            return false;
        }

        _toast($"已移到「{TaskBoard.StatusTitle(target)}」", false);
        _dataChanged?.Invoke();
        Reload();
        return true;
    }

    /// <summary>拖拽落点：把项目移到目标列。</summary>
    public bool MoveProject(string projectId, ProjectStatus target)
    {
        var current = _service.ProjectsGet(projectId);
        if (!current.Ok)
        {
            _toast(current.Error?.Message ?? "项目读取失败", true);
            Reload();
            return false;
        }

        if (current.Data is null)
        {
            _toast("项目已不存在，看板已刷新", true);
            Reload();
            return false;
        }

        if (current.Data.Status == target)
        {
            Reload();
            return false;
        }

        var result = _service.ProjectsUpdate(
            projectId,
            new UpdateProjectInput { Status = Optional<ProjectStatus>.Some(target) });

        if (!result.Ok)
        {
            _toast(result.Error?.Message ?? "更新项目状态失败", true);
            Reload();
            return false;
        }

        _toast($"项目已改为「{BoardProjectColumnViewModel.TitleOf(target)}」", false);
        _dataChanged?.Invoke();
        Reload();
        return true;
    }

    public void OpenTask(TaskItem task) => EditTaskRequested?.Invoke(task);

    private void OpenProjectTasks(BoardProjectCardViewModel card) =>
        OpenProjectTasksRequested?.Invoke(card.Project.Id);
}

/// <summary>
/// 概览条 / 筛选胶囊。做成对象而不是 record，才能直接绑选中态与点击命令。
/// </summary>
public sealed partial class BoardChip : ObservableObject
{
    public BoardChip(string key, string label, Action<BoardChip> select, bool showCount = false)
    {
        Key = key;
        Label = label;
        ShowCount = showCount;
        SelectCommand = new RelayCommand(() => select(this));
    }

    public string Key { get; }

    public string Label { get; }

    /// <summary>概览条上的计数；筛选胶囊不显示数字。</summary>
    public bool ShowCount { get; }

    public IRelayCommand SelectCommand { get; }

    public string CountText => Count.ToString(CultureInfo.InvariantCulture);

    /// <summary>
    /// 概览条数字的颜色。只有「逾期」在非零时才用语义色 ——
    /// 每个数字都染色等于没有重点。
    /// </summary>
    public IBrush CountBrush => (Key, Count > 0) switch
    {
        ("overdue", true) => ThemeBrushes.Get("SukiDangerColor"),
        _ => ThemeBrushes.Get("SukiText")
    };

    [ObservableProperty]
    private int _count;

    [ObservableProperty]
    private bool _isSelected;

    partial void OnCountChanged(int value)
    {
        OnPropertyChanged(nameof(CountText));
        OnPropertyChanged(nameof(CountBrush));
    }
}

/// <summary>任务看板的一列。</summary>
public sealed partial class BoardColumnViewModel : ObservableObject
{
    private readonly Action _toggleDoneWindow;

    public BoardColumnViewModel(
        TaskStatus status,
        IReadOnlyList<BoardTaskCardViewModel> cards,
        Action toggleDoneWindow,
        bool showAllDone = false)
    {
        Status = status;
        Title = TaskBoard.StatusTitle(status);
        Cards = new ObservableCollection<BoardTaskCardViewModel>(cards);
        _toggleDoneWindow = toggleDoneWindow;
        ShowAllDone = showAllDone;
    }

    public TaskStatus Status { get; }

    public string Title { get; }

    public ObservableCollection<BoardTaskCardViewModel> Cards { get; }

    public bool IsDone => Status == TaskStatus.Done;

    /// <summary>四种状态各用一个 Suki 语义色，列头色带 / 标题 / 边框 / 淡底色都取它。</summary>
    private string StatusColorKey => Status switch
    {
        TaskStatus.Todo => "SukiMuteText",
        TaskStatus.Doing => "SukiPrimaryColor",
        TaskStatus.Blocked => "SukiDangerColor",
        TaskStatus.Done => "SukiSuccessColor",
        _ => "SukiDisabledText"
    };

    public IBrush StatusBrush => ThemeBrushes.Get(StatusColorKey);

    public IBrush StatusTint => ThemeBrushes.Tint(StatusColorKey);

    /// <summary>「已完成」列的窗口是否已放宽到全部历史。</summary>
    public bool ShowAllDone { get; }

    public string DoneWindowLabel => ShowAllDone ? "只看近 7 天" : "显示全部";

    public int Count => Cards.Count;

    public string CountText => Count.ToString(CultureInfo.InvariantCulture);

    public bool IsEmpty => Count == 0;

    /// <summary>拖拽悬停高亮。视图层在 DragOver 时置位、离开或落下时复位。</summary>
    [ObservableProperty]
    private bool _isDragOver;

    [RelayCommand]
    private void ToggleDoneWindow() => _toggleDoneWindow();
}

/// <summary>项目看板的一列。</summary>
public sealed partial class BoardProjectColumnViewModel : ObservableObject
{
    /// <summary>项目看板列顺序，与设计文档的「规划中 / 进行中 / 暂停 / 已完成」一致。</summary>
    public static readonly IReadOnlyList<ProjectStatus> StatusOrder =
    [
        ProjectStatus.Planning,
        ProjectStatus.Active,
        ProjectStatus.Paused,
        ProjectStatus.Done
    ];

    public BoardProjectColumnViewModel(
        ProjectStatus status,
        IReadOnlyList<BoardProjectCardViewModel> cards)
    {
        Status = status;
        Title = TitleOf(status);
        Cards = new ObservableCollection<BoardProjectCardViewModel>(cards);
    }

    public ProjectStatus Status { get; }

    public string Title { get; }

    /// <summary>规划 / 进行 / 暂停 / 完成分别用中性、主色、警示、成功色。</summary>
    private string StatusColorKey => Status switch
    {
        ProjectStatus.Planning => "SukiMuteText",
        ProjectStatus.Active => "SukiPrimaryColor",
        ProjectStatus.Paused => "SukiWarningColor",
        _ => "SukiSuccessColor"
    };

    public IBrush StatusBrush => ThemeBrushes.Get(StatusColorKey);

    public IBrush StatusTint => ThemeBrushes.Tint(StatusColorKey);

    public ObservableCollection<BoardProjectCardViewModel> Cards { get; }

    public int Count => Cards.Count;

    public string CountText => Count.ToString(CultureInfo.InvariantCulture);

    public bool IsEmpty => Count == 0;

    [ObservableProperty]
    private bool _isDragOver;

    public static string TitleOf(ProjectStatus status) => status switch
    {
        ProjectStatus.Planning => "规划中",
        ProjectStatus.Active => "进行中",
        ProjectStatus.Paused => "暂停",
        _ => "已完成"
    };
}

/// <summary>看板任务卡片。</summary>
public sealed class BoardTaskCardViewModel
{
    private static IBrush NeutralDot => ThemeBrushes.Get("SukiMuteText");

    public BoardTaskCardViewModel(TaskItem task, string? projectName, string? projectColor, DateTime now)
    {
        Task = task;
        ProjectName = projectName;
        ProjectBrush = projectColor is null ? NeutralDot : ParseColor(projectColor);
        IsOverdue = task.Status is not (TaskStatus.Done or TaskStatus.Canceled)
                    && Formatting.IsOverdue(task.DueAt, LocalTime.ToLocalDateTime(now));
    }

    public TaskItem Task { get; }

    public string Title => Task.Title;

    public string? ProjectName { get; }

    public IBrush ProjectBrush { get; }

    public bool HasProject => ProjectName is not null;

    public bool IsOverdue { get; }

    public bool HasDue => !string.IsNullOrEmpty(Task.DueAt);

    public string DueText => Formatting.FormatLocalDateTime(Task.DueAt);

    public bool HasPriority => Task.Priority != Priority.None;

    public IBrush PriorityBrush => Task.Priority switch
    {
        Priority.High => ThemeBrushes.Get("SukiDangerColor"),
        Priority.Medium => ThemeBrushes.Get("SukiWarningColor"),
        Priority.Low => ThemeBrushes.Get("SukiMuteText"),
        _ => Brushes.Transparent
    };

    public string PriorityText => Task.Priority switch
    {
        Priority.High => "高",
        Priority.Medium => "中",
        Priority.Low => "低",
        _ => string.Empty
    };

    public string EstimateText => Task.Estimate is null
        ? string.Empty
        : $"预估 {Formatting.FormatHours(Task.Estimate)}";

    public bool HasEstimate => Task.Estimate is not null;

    public string BlockedText => string.IsNullOrWhiteSpace(Task.BlockedReason)
        ? string.Empty
        : $"阻塞：{Task.BlockedReason}";

    public bool HasBlockedReason => Task.Status == TaskStatus.Blocked && BlockedText.Length > 0;

    /// <summary>卡片底部的一行元信息。合成一个字符串，避免在 XAML 里拼分隔符。</summary>
    public string FooterText
    {
        get
        {
            var parts = new List<string>(3);
            if (HasDue) parts.Add(DueText);
            if (HasEstimate) parts.Add(EstimateText);
            if (HasPriority) parts.Add($"{PriorityText}优先级");
            return string.Join(" · ", parts);
        }
    }

    public bool HasFooter => FooterText.Length > 0;

    /// <summary>底部元信息色：逾期拉红，平时弱化。</summary>
    public IBrush FooterBrush => IsOverdue
        ? ThemeBrushes.Get("SukiDangerColor")
        : ThemeBrushes.Get("SukiMuteText");

    private static IBrush ParseColor(string color)
    {
        try
        {
            return Brush.Parse(color);
        }
        catch (FormatException)
        {
            return NeutralDot;
        }
    }
}

/// <summary>看板项目卡片。进度环与统计口径和项目页共用 <see cref="ProjectSummary"/>。</summary>
public sealed class BoardProjectCardViewModel
{
    private readonly Action<BoardProjectCardViewModel> _openTasks;

    public BoardProjectCardViewModel(
        Project project,
        IReadOnlyList<TaskItem> tasks,
        DateTime now,
        Action<BoardProjectCardViewModel> openTasks)
    {
        Project = project;
        _accent = ParseColor(project.Color);
        _openTasks = openTasks;

        var (counts, progress) = ProjectSummary.Compute(project, tasks, now);
        Counts = counts;
        Progress = progress;
    }

    /// <summary>卡片自己持有命令，XAML 里不用绕回父级 DataContext 取 Column 的命令。</summary>
    public IRelayCommand OpenTasksCommand => _openTasksCommand ??= new RelayCommand(() => _openTasks(this));

    private IRelayCommand? _openTasksCommand;

    public Project Project { get; }

    public TaskCounts Counts { get; }

    public int Progress { get; }

    public string Name => Project.Name;

    public string ProgressText => $"{Progress}%";

    /// <summary>
    /// 卡片上的人数只留「任务总数」。看板列最窄到 170 出头，
    /// 再多一段「进行中 N」会被裁成半句，反而比不写更糟 —— 进行中的量由列的分布本身表达。
    /// </summary>
    public string MetaText => $"{Counts.Total} 个任务";

    public string OverdueText => Counts.Overdue > 0 ? $"逾期 {Counts.Overdue}" : string.Empty;

    public bool HasOverdue => Counts.Overdue > 0;

    public bool HasDescription => !string.IsNullOrWhiteSpace(Project.Description);

    public string Description => Project.Description;

    /// <summary>有没有优先级。与任务卡一致：无优先级时不画标识。</summary>
    public bool HasPriority => Project.Priority != Priority.None;

    /// <summary>优先级色条的颜色，与任务卡完全一致。</summary>
    public IBrush PriorityBrush => Project.Priority switch
    {
        Priority.High => ThemeBrushes.Get("SukiDangerColor"),
        Priority.Medium => ThemeBrushes.Get("SukiWarningColor"),
        Priority.Low => ThemeBrushes.Get("SukiMuteText"),
        _ => Brushes.Transparent
    };

    /// <summary>优先级文字（高/中/低）。</summary>
    public string PriorityText => Project.Priority switch
    {
        Priority.High => "高",
        Priority.Medium => "中",
        Priority.Low => "低",
        _ => string.Empty
    };

    /// <summary>项目的起止区间。与项目页卡片同一套文案与口径。</summary>
    public string DateRangeText
    {
        get
        {
            var start = Project.StartDate;
            var due = Project.DueDate;

            if (!string.IsNullOrEmpty(start) && !string.IsNullOrEmpty(due)) return $"{start} ~ {due}";
            if (!string.IsNullOrEmpty(due)) return $"结束 {due}";
            if (!string.IsNullOrEmpty(start)) return $"开始 {start}";
            return "未设置起止时间";
        }
    }

    public IBrush AccentBrush => _accent ?? Brushes.Transparent;

    public bool HasColor => _accent is not null;

    private readonly IBrush? _accent;

    private static IBrush? ParseColor(string? color)
    {
        if (string.IsNullOrWhiteSpace(color)) return null;
        try
        {
            return Brush.Parse(color);
        }
        catch (FormatException)
        {
            return null;
        }
    }
}
