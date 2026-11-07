using System.Collections.ObjectModel;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WorkStation.App.Controls;
using WorkStation.Core;
using WorkStation.Core.Board;
using WorkStation.Core.Dashboard;
using WorkStation.Core.Models;
using WorkStation.Core.Services;
using WorkStation.Core.Time;

namespace WorkStation.App.ViewModels;

/// <summary>仪表盘跳转的目标口径 —— 与看板的筛选一一对应。</summary>
public enum DashboardJumpTarget
{
    /// <summary>不筛选，全部任务。</summary>
    All,

    /// <summary>今日到期。</summary>
    Today,

    /// <summary>逾期未完成。</summary>
    Overdue,

    /// <summary>本周到期。</summary>
    Week,

    /// <summary>只看已阻塞。</summary>
    Blocked
}

/// <summary>
/// 仪表盘 —— 只读统计页。
///
/// 与看板的分工：看板是「操作台」（筛选、拖状态、编辑卡片），
/// 仪表盘是「观测台」（只报数、画趋势）。这里刻意**不放任何筛选控件**，
/// 避免和看板的今日/逾期/本周胶囊重复；数字点一下是跳去看板并带上口径。
/// </summary>
public sealed partial class DashboardViewModel : ObservableObject
{
    private readonly WorkStationService _service;
    private readonly Func<DateTime> _clock;
    private readonly Action<string, bool> _toast;

    /// <summary>点击统计卡片时通知外壳：跳到看板并带上口径。</summary>
    public Action<DashboardJumpTarget>? JumpToBoardRequested { get; set; }

    /// <summary>点项目：通知外壳弹出该项目的任务清单。</summary>
    public Action<string>? OpenProjectRequested { get; set; }

    /// <summary>点任务：通知外壳打开该任务的编辑弹窗。</summary>
    public Action<TaskItem>? OpenTaskRequested { get; set; }

    public DashboardViewModel(
        WorkStationService service,
        Action<string, bool> toast,
        Func<DateTime>? clock = null)
    {
        _service = service;
        _toast = toast;
        _clock = clock ?? (() => DateTime.Now);

        Periods =
        [
            new DashboardPeriodViewModel("今日", DashboardRange.Today),
            new DashboardPeriodViewModel("本周", DashboardRange.Week),
            new DashboardPeriodViewModel("本月", DashboardRange.Month)
        ];

        // 默认选中「今日」：进仪表盘先看今天要扛多少活。
        // 清单是常驻的（同一时刻必然有一张被选中），不再有「展开/收起」两种状态。
        Periods[0].IsSelected = true;

        Trend = [];
        StatusBreakdown = [];
        PriorityBreakdown = [];
        Projects = [];
        UpcomingTasks = [];
    }

    public ObservableCollection<DashboardPeriodViewModel> Periods { get; }

    public ObservableCollection<AppTrendBar> Trend { get; }

    public ObservableCollection<DashboardStatusSliceViewModel> StatusBreakdown { get; }

    public ObservableCollection<DashboardPrioritySliceViewModel> PriorityBreakdown { get; }

    /// <summary>各项目的进度与任务数（实体数据，不是图表）。</summary>
    public ObservableCollection<DashboardProjectViewModel> Projects { get; }

    /// <summary>近期要处理的任务清单（实体数据，不是图表）。</summary>
    public ObservableCollection<DashboardTaskViewModel> UpcomingTasks { get; }

    /// <summary>有没有项目可显示。</summary>
    public bool HasProjects => Projects.Count > 0;

    /// <summary>有没有近期任务可显示。</summary>
    public bool HasUpcomingTasks => UpcomingTasks.Count > 0;

    [ObservableProperty]
    private int _overdueCount;

    [ObservableProperty]
    private int _blockedCount;

    /// <summary>逾期 + 阻塞：有才显示提示条。</summary>
    public bool HasAttention => OverdueCount + BlockedCount > 0;

    /// <summary>提示条正文：逾期与阻塞各多少。</summary>
    public string AttentionText =>
        $"逾期 {OverdueCount} 项 · 已阻塞 {BlockedCount} 项";

    [ObservableProperty]
    private bool _isEmpty = true;

    /// <summary>总任务数（不含已取消）。</summary>
    [ObservableProperty]
    private int _totalTasks;

    /// <summary>未完成任务数。</summary>
    [ObservableProperty]
    private int _openTasks;

    partial void OnOverdueCountChanged(int value)
    {
        OnPropertyChanged(nameof(HasAttention));
        OnPropertyChanged(nameof(AttentionText));
    }

    partial void OnBlockedCountChanged(int value)
    {
        OnPropertyChanged(nameof(HasAttention));
        OnPropertyChanged(nameof(AttentionText));
    }

    /// <summary>取数刷新。口径全部来自 <see cref="DashboardStats"/>，与看板一致。</summary>
    public void Reload()
    {
        try
        {
            var tasks = _service.TasksList().Data ?? [];
            var now = _clock();

            var ranges = DashboardStats.ComputeRanges(tasks, now);
            foreach (var period in Periods)
            {
                if (!ranges.TryGetValue(period.Range, out var stats)) continue;

                period.Apply(stats);
                period.Tasks.Clear();
                foreach (var item in DashboardStats.TasksInRange(tasks, period.Range, now))
                {
                    period.Tasks.Add(new DashboardTaskItem
                    {
                        Title = item.Title,
                        DueAt = item.DueAt ?? string.Empty,
                        IsOverdue = item.DueAt is not null
                            && Formatting.IsOverdue(item.DueAt, Core.Time.LocalTime.ToLocalDateTime(now)),
                        Priority = item.Priority,
                        StatusLabel = TaskBoard.StatusTitle(item.Status),
                        // 行尾补一个日期，但「今天到期」的不写 —— 看「今日」口径时每行都写 11-06
                        // 只是噪声；看「本月」时日期才是真正需要的信息。
                        DateLabel = FormatDueDate(item.DueAt, now)
                    });
                }

                period.NotifyTasksChanged();
            }

            Replace(Trend, DashboardStats.ComputeTrend(tasks, now).Select(point => new AppTrendBar(
                point.Label,
                point.Completed,
                point.Due,
                point.CompletedRatio,
                point.DueRatio,
                point.IsToday)));
            Replace(
                StatusBreakdown,
                DashboardStats.ComputeStatusBreakdown(tasks).Select(s => new DashboardStatusSliceViewModel
                {
                    Label = s.Label,
                    Count = s.Count,
                    Ratio = s.Ratio,
                    Status = s.Status
                }));
            Replace(
                PriorityBreakdown,
                DashboardStats.ComputePriorityBreakdown(tasks).Select(p => new DashboardPrioritySliceViewModel
                {
                    Label = p.Label,
                    Count = p.Count,
                    Ratio = p.Ratio,
                    Priority = p.Priority
                }));

            var (overdue, blocked) = DashboardStats.ComputeAttention(tasks, now);
            OverdueCount = overdue;
            BlockedCount = blocked;

            // 实体数据：项目进度 + 近期任务
            LoadProjects(tasks, now);
            LoadUpcomingTasks(tasks, now);

            TotalTasks = tasks.Count(t => t.Status != TaskStatus.Canceled);
            OpenTasks = tasks.Count(t => t.Status is not (TaskStatus.Done or TaskStatus.Canceled));
            IsEmpty = TotalTasks == 0;

            OnPropertyChanged(nameof(HasAttention));
        }
        catch (Exception ex)
        {
            _toast($"读取统计失败：{ex.Message}", true);
        }
    }

    /// <summary>
    /// 清单行尾的日期：没有截止时间、或者就是今天，都返回空字符串（今天不用再提醒一遍）。
    /// 其它日期写成 MM-dd；跨年的写成 yyyy-MM-dd，避免「01-03」看不出是哪年。
    /// </summary>
    private static string FormatDueDate(string? dueAt, DateTime now)
    {
        if (string.IsNullOrWhiteSpace(dueAt)) return string.Empty;

        // 墙钟串固定是 yyyy-MM-ddTHH:mm，直接切前 10 位拿日期，不走本地化解析
        var value = dueAt.Trim();
        if (value.Length < 10) return string.Empty;

        var date = value.Substring(0, 10);
        var today = now.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
        if (date == today) return string.Empty;

        return value.Substring(0, 4) == now.ToString("yyyy", System.Globalization.CultureInfo.InvariantCulture)
            ? date.Substring(5)
            : date;
    }

    /// <summary>
    /// 点统计卡：切换下方清单显示哪段时间的任务。
    ///
    /// 清单是**常驻**的 —— 再点一次当前这张不会收起。
    /// 收起会让左栏只剩一条提示、右栏还是整块项目卡，页面上直接空出一大块，
    /// 而且每点一次页面高度就跳一次。所以这里只做「选择」，不做「开/关」。
    ///
    /// 刻意**不跳看板** —— 仪表盘的口径是「这段时间要扛多少活」（按截止日期累计），
    /// 看板的口径是「现在该做什么」（按 TaskGrouping 分组），两者数字本来就不相等。
    /// 跳过去会让用户以为看错了。清单则保证「卡片数字 = 清单条数」，永远对得上。
    /// </summary>
    [RelayCommand]
    private void SelectPeriod(DashboardPeriodViewModel? period)
    {
        if (period is null || period.IsSelected) return;

        foreach (var other in Periods) other.IsSelected = false;
        period.IsSelected = true;

        OnPropertyChanged(nameof(SelectedPeriod));
    }

    /// <summary>当前清单显示的时间窗口。构造时就选好了，永远有一张。</summary>
    public DashboardPeriodViewModel SelectedPeriod =>
        Periods.FirstOrDefault(p => p.IsSelected) ?? Periods[0];

    /// <summary>点逾期数：跳去看板的逾期口径（口径与看板完全一致）。</summary>
    [RelayCommand]
    private void JumpToOverdue() => JumpToBoardRequested?.Invoke(DashboardJumpTarget.Overdue);

    /// <summary>点阻塞数：跳去看板并只看已阻塞。</summary>
    [RelayCommand]
    private void JumpToBlocked() => JumpToBoardRequested?.Invoke(DashboardJumpTarget.Blocked);

    /// <summary>点项目：弹该项目的任务清单（与项目页「查看任务」同一套弹窗）。</summary>
    [RelayCommand]
    private void OpenProject(DashboardProjectViewModel? project)
    {
        if (project is null) return;

        OpenProjectRequested?.Invoke(project.Id);
    }

    /// <summary>点任务：打开编辑弹窗（与看板双击卡片同一套弹窗）。</summary>
    [RelayCommand]
    private void OpenTask(DashboardTaskViewModel? task)
    {
        if (task?.Source is null) return;

        OpenTaskRequested?.Invoke(task.Source);
    }

    /// <summary>载入各项目进度。口径复用项目页的那份统计，数字必须一致。</summary>
    private void LoadProjects(IReadOnlyList<TaskItem> tasks, DateTime now)
    {
        var result = _service.ProjectsList();
        var projects = result.Ok ? result.Data ?? [] : [];

        Replace(
            Projects,
            DashboardStats.ComputeProjectProgress(projects, tasks, now)
                .Select(p => new DashboardProjectViewModel
                {
                    Id = p.Id,
                    Name = p.Name,
                    StatusLabel = p.StatusLabel,
                    Progress = p.Progress,
                    Total = p.Total,
                    Done = p.Done,
                    Doing = p.Doing,
                    Overdue = p.Overdue
                }));

        OnPropertyChanged(nameof(HasProjects));
    }

    /// <summary>载入近期任务清单：逾期的排最前，其次按截止时间近的。</summary>
    private void LoadUpcomingTasks(IReadOnlyList<TaskItem> tasks, DateTime now)
    {
        var names = new Dictionary<string, string>(StringComparer.Ordinal);
        var projects = _service.ProjectsList();
        if (projects.Ok && projects.Data is not null)
        {
            foreach (var project in projects.Data)
            {
                names[project.Id] = project.Name;
            }
        }

        var byId = tasks.ToDictionary(t => t.Id, t => t);

        Replace(
            UpcomingTasks,
            DashboardStats.ComputeUpcomingTasks(tasks, names, now)
                .Select(row => new DashboardTaskViewModel
                {
                    Source = byId.TryGetValue(row.Id, out var source) ? source : null,
                    Title = row.Title,
                    ProjectName = row.ProjectName,
                    StatusLabel = row.StatusLabel,
                    Priority = row.Priority,
                    DueText = Formatting.FormatLocalDateTime(row.DueAt),
                    IsOverdue = row.IsOverdue
                }));

        OnPropertyChanged(nameof(HasUpcomingTasks));
    }

    private static void Replace<T>(ObservableCollection<T> target, IEnumerable<T> items)
    {
        target.Clear();
        foreach (var item in items) target.Add(item);
    }
}

/// <summary>一个时间窗口的展示模型（今日/本周/本月）。</summary>
public sealed partial class DashboardPeriodViewModel : ObservableObject
{
    public DashboardPeriodViewModel(string label, DashboardRange range)
    {
        Label = label;
        Range = range;
    }

    public string Label { get; }

    public DashboardRange Range { get; }

    /// <summary>窗口内到期的未完成任务数。</summary>
    [ObservableProperty]
    private int _due;

    /// <summary>窗口内完成的任务数。</summary>
    [ObservableProperty]
    private int _completed;

    /// <summary>完成率 0–100。</summary>
    [ObservableProperty]
    private int _completionRate;

    /// <summary>
    /// 该窗口内到期的未完成任务清单。
    /// 点卡片展开的就是它 —— 数字与清单严格一致，不会出现「点过去对不上」。
    /// </summary>
    public ObservableCollection<DashboardTaskItem> Tasks { get; } = [];

    public void Apply(DashboardRangeStats stats)
    {
        Due = stats.Due;
        Completed = stats.Completed;
        CompletionRate = stats.CompletionRate;
    }

    /// <summary>完成率对应的圆环进度（0–100）。</summary>
    public double Progress => CompletionRate;

    /// <summary>副标题：完成 X / 到期 Y。</summary>
    public string Summary => $"完成 {Completed} · 到期 {Due}";

    /// <summary>清单是否为空（空清单给一句说明，别留一块白）。</summary>
    public bool HasTasks => Tasks.Count > 0;

    /// <summary>这张卡是不是当前选中的那张（同一时刻必然有一张）。</summary>
    [ObservableProperty]
    private bool _isSelected;

    /// <summary>清单条数变化后要通知视图刷新「展开/收起」提示。</summary>
    public void NotifyTasksChanged() => OnPropertyChanged(nameof(HasTasks));

    partial void OnDueChanged(int value) => OnPropertyChanged(nameof(Summary));

    partial void OnCompletedChanged(int value) => OnPropertyChanged(nameof(Summary));

    partial void OnCompletionRateChanged(int value) => OnPropertyChanged(nameof(Progress));
}

/// <summary>仪表盘清单里的一条任务。</summary>
public sealed class DashboardTaskItem
{    public required string Title { get; init; }

    /// <summary>截止时间（本地墙钟 YYYY-MM-DDTHH:mm）。没有就空。</summary>
    public string DueAt { get; init; } = string.Empty;

    /// <summary>是否已逾期。</summary>
    public bool IsOverdue { get; init; }

    /// <summary>优先级（高/中/低/无），用于配色。</summary>
    public required Priority Priority { get; init; }

    /// <summary>状态文案。</summary>
    public required string StatusLabel { get; init; }

    /// <summary>行尾日期（MM-dd）。今天到期或没有截止时间时为空，不占位置。</summary>
    public string DateLabel { get; init; } = string.Empty;

    public bool HasDateLabel => DateLabel.Length > 0;

    /// <summary>优先级/状态的小圆点颜色。</summary>
    public IBrush DotBrush => Priority switch
    {
        Priority.High => ThemeBrushes.Get("SukiDangerColor"),
        Priority.Medium => ThemeBrushes.Get("SukiWarningColor"),
        Priority.Low => ThemeBrushes.Get("SukiPrimaryColor"),
        _ => ThemeBrushes.Get("SukiMuteText")
    };
}

/// <summary>状态分布条目的展示模型。</summary>
public sealed class DashboardStatusSliceViewModel
{
    public required string Label { get; init; }

    public required int Count { get; init; }

    /// <summary>占比 0–1，绑 ProgressBar 的 Value。</summary>
    public required double Ratio { get; init; }

    /// <summary>ProgressBar 的上限固定为 100，占比直接当百分比用。</summary>
    public double Percent => Ratio * 100;

    public IBrush Brush => Status switch
    {
        TaskStatus.Doing => ThemeBrushes.Get("SukiPrimaryColor"),
        TaskStatus.Blocked => ThemeBrushes.Get("SukiWarningColor"),
        TaskStatus.Done => ThemeBrushes.Get("SukiSuccessColor"),
        _ => ThemeBrushes.Get("SukiMuteText")
    };

    public required TaskStatus Status { get; init; }
}

/// <summary>优先级分布条目的展示模型。</summary>
public sealed class DashboardPrioritySliceViewModel
{
    public required string Label { get; init; }

    public required int Count { get; init; }

    public required double Ratio { get; init; }

    /// <summary>ProgressBar 的百分比值。</summary>
    public double Percent => Ratio * 100;

    public IBrush Brush => Priority switch
    {
        Priority.High => ThemeBrushes.Get("SukiDangerColor"),
        Priority.Medium => ThemeBrushes.Get("SukiWarningColor"),
        Priority.Low => ThemeBrushes.Get("SukiPrimaryColor"),
        _ => ThemeBrushes.Get("SukiMuteText")
    };

    public required Priority Priority { get; init; }
}

/// <summary>仪表盘上的一个项目（带进度环与任务计数）。</summary>
public sealed class DashboardProjectViewModel
{
    public required string Id { get; init; }

    public required string Name { get; init; }

    /// <summary>项目状态文案（规划中/进行中/已暂停/已完成）。</summary>
    public required string StatusLabel { get; init; }

    /// <summary>完成进度 0–100。</summary>
    public required int Progress { get; init; }

    public required int Total { get; init; }

    public required int Done { get; init; }

    public required int Doing { get; init; }

    public required int Overdue { get; init; }

    /// <summary>进度环上的百分比文字。</summary>
    public string ProgressText => $"{Progress}%";

    /// <summary>任务计数：3 个任务 · 进行中 1。</summary>
    public string MetaText => $"{Total} 个任务 · 进行中 {Doing}";

    /// <summary>有逾期才显示。</summary>
    public string? OverdueText => Overdue > 0 ? $"逾期 {Overdue}" : null;

    public bool HasOverdue => Overdue > 0;

    /// <summary>状态圆点颜色，跟项目页卡片一致。</summary>
    public IBrush StatusBrush => StatusLabel switch
    {
        "进行中" => ThemeBrushes.Get("SukiPrimaryColor"),
        "已暂停" => ThemeBrushes.Get("SukiWarningColor"),
        "已完成" => ThemeBrushes.Get("SukiSuccessColor"),
        _ => ThemeBrushes.Get("SukiMuteText")
    };
}

/// <summary>仪表盘「近期任务」里的一行。</summary>
public sealed class DashboardTaskViewModel
{
    /// <summary>原始任务 —— 点它要能打开编辑弹窗。找不到对应任务时为 null（此时不可点）。</summary>
    public TaskItem? Source { get; init; }

    public required string Title { get; init; }

    /// <summary>所属项目名；没有项目则为空。</summary>
    public string? ProjectName { get; init; }

    public required string StatusLabel { get; init; }

    public required Priority Priority { get; init; }

    /// <summary>截止时间（已格式化成 MM-DD HH:mm）。</summary>
    public string DueText { get; init; } = string.Empty;

    public bool IsOverdue { get; init; }

    /// <summary>有没有项目名（决定要不要显示那截标签）。</summary>
    public bool HasProject => !string.IsNullOrEmpty(ProjectName);

    /// <summary>优先级/状态的小圆点颜色。</summary>
    public IBrush DotBrush => Priority switch
    {
        Priority.High => ThemeBrushes.Get("SukiDangerColor"),
        Priority.Medium => ThemeBrushes.Get("SukiWarningColor"),
        Priority.Low => ThemeBrushes.Get("SukiPrimaryColor"),
        _ => ThemeBrushes.Get("SukiMuteText")
    };
}
