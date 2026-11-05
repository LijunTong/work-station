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

        OverviewChips =
        [
            new BoardChip("today", "今日", SelectOverview, showCount: true),
            new BoardChip("overdue", "逾期", SelectOverview, showCount: true),
            new BoardChip("blocked", "已阻塞", SelectOverview, showCount: true),
            new BoardChip("week", "本周", SelectOverview, showCount: true)
        ];

        PriorityChips =
        [
            new BoardChip("all", "全部优先级", chip => SelectedPriority = chip),
            new BoardChip("high", "高", chip => SelectedPriority = chip),
            new BoardChip("medium", "中", chip => SelectedPriority = chip),
            new BoardChip("low", "低", chip => SelectedPriority = chip)
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
            OverviewChips[0].Count = overview.Today;
            OverviewChips[1].Count = overview.Overdue;
            OverviewChips[2].Count = overview.Blocked;
            OverviewChips[3].Count = overview.Week;

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
                    ? matching.OrderByDescending(t => t.CompletedAt ?? t.UpdatedAt, StringComparer.Ordinal)
                    : matching
                        .OrderBy(t => string.IsNullOrEmpty(t.DueAt) ? 1 : 0)
                        .ThenBy(t => t.DueAt, StringComparer.Ordinal)
                        .ThenBy(t => t.CreatedAt, StringComparer.Ordinal))
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
                .OrderByDescending(p => p.Priority)
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

        // Clear() 会让绑定中的 ComboBox 把 SelectedItem 回写成 null；重建期间吞掉由此触发的 Reload
        _isRebuildingOptions = true;
        try
        {
            ProjectOptions.Clear();
            ProjectOptions.Add(ProjectOption.All);
            foreach (var project in projects.Where(p => p.ArchivedAt is null).DistinctBy(p => p.Id))
            {
                ProjectOptions.Add(new ProjectOption(project.Id, project.Name));
            }

            SelectedProject = ProjectOptions.FirstOrDefault(o => o.Id == previous) ?? ProjectOptions[0];
        }
        finally
        {
            _isRebuildingOptions = false;
        }
    }

    private void SelectOverview(BoardChip chip)
    {
        switch (chip.Key)
        {
            case "blocked":
                OnlyBlocked = !OnlyBlocked;
                break;
            case "today":
                TimeScope = TimeScope == BoardTimeScope.Today ? BoardTimeScope.All : BoardTimeScope.Today;
                break;
            case "overdue":
                TimeScope = TimeScope == BoardTimeScope.Overdue ? BoardTimeScope.All : BoardTimeScope.Overdue;
                break;
            case "week":
                TimeScope = TimeScope == BoardTimeScope.Week ? BoardTimeScope.All : BoardTimeScope.Week;
                break;
        }

        SyncOverviewChips();
        ReloadIfNeeded();
    }

    private void SyncOverviewChips()
    {
        OverviewChips[0].IsSelected = TimeScope == BoardTimeScope.Today;
        OverviewChips[1].IsSelected = TimeScope == BoardTimeScope.Overdue;
        OverviewChips[2].IsSelected = OnlyBlocked;
        OverviewChips[3].IsSelected = TimeScope == BoardTimeScope.Week;
    }

    [RelayCommand]
    private void SwitchBoard(string mode)
    {
        IsTaskBoard = !string.Equals(mode, nameof(BoardMode.Projects), StringComparison.OrdinalIgnoreCase);
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
    /// 概览条数字的颜色。只有「逾期」「已阻塞」在非零时才用语义色 ——
    /// 每个数字都染色等于没有重点。
    /// </summary>
    public IBrush CountBrush => (Key, Count > 0) switch
    {
        ("overdue", true) => ThemeBrushes.Get("DangerBrush", "#C0392F"),
        ("blocked", true) => ThemeBrushes.Get("WarningBrush", "#9A5B06"),
        _ => ThemeBrushes.Get("TextBrush", "#12161D")
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
    private static readonly IBrush NeutralDot = Brush.Parse("#8B95A6");

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
        Priority.High => Brush.Parse("#D9534F"),
        Priority.Medium => Brush.Parse("#D9932B"),
        Priority.Low => Brush.Parse("#8A94A6"),
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

    public string DueText
    {
        get
        {
            var due = string.IsNullOrEmpty(Project.DueDate)
                ? "无目标日期"
                : $"目标 {Formatting.FormatLocalDateTime(Project.DueDate)}";

            // 逾期数字在左边单独用红色显示，这里补一个分隔点，避免「逾期 3 目标 11-05」糊成一句
            return HasOverdue ? $"· {due}" : due;
        }
    }

    public IBrush AccentBrush => _accent ?? Brushes.Transparent;

    public bool HasColor => _accent is not null;

    /// <summary>进度环跟随项目色；无色时回落到强调色，否则整个环会解析成透明而消失。</summary>
    public IBrush RingBrush => _accent ?? AccentFallback;

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

    private static IBrush AccentFallback => ThemeBrushes.Get("AccentBrush", "#2F62E8");
}
