using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WorkStation.Core;
using WorkStation.Core.Models;
using WorkStation.Core.Services;
using WorkStation.Core.Time;

namespace WorkStation.App.ViewModels;

/// <summary>命令面板里的一条静态动作：跳页面、新建、看帮助。</summary>
public sealed record PaletteAction(
    string Title,
    string Hint,
    string Glyph,
    IReadOnlyList<string> Keywords,
    Action Invoke)
{
    public bool Matches(string query) =>
        Title.Contains(query, StringComparison.OrdinalIgnoreCase)
        || Keywords.Any(k => k.Contains(query, StringComparison.OrdinalIgnoreCase));
}

/// <summary>
/// 命令面板里的一行结果。行自带「选中态 + 执行命令」，
/// 视图不必在 XAML 里绕回父级 DataContext 取命令。
/// </summary>
public sealed partial class PaletteItem : ObservableObject
{
    public PaletteItem(string title, string hint, string glyph, Action invoke, Action<PaletteItem> activate)
    {
        Title = title;
        Hint = hint;
        Glyph = glyph;
        Invoke = invoke;
        ActivateCommand = new RelayCommand(() => activate(this));
    }

    public string Title { get; }

    public string Hint { get; }

    public string Glyph { get; }

    public Action Invoke { get; }

    public IRelayCommand ActivateCommand { get; }

    [ObservableProperty]
    private bool _isSelected;
}

/// <summary>
/// Ctrl+K 命令面板：一个输入框同时承担「跳转、执行动作、找任务/项目」。
///
/// 空查询时给出动作 + 最近要做的几件事，开始输入后换成模糊匹配结果 ——
/// 命令面板最大的失败是打开后一片空白，逼用户先想清楚要搜什么。
/// </summary>
public partial class CommandPaletteViewModel : ObservableObject
{
    private const int MaxTasks = 8;
    private const int MaxProjects = 5;

    private readonly WorkStationService _service;
    private readonly IReadOnlyList<PaletteAction> _actions;

    public CommandPaletteViewModel(WorkStationService service, IReadOnlyList<PaletteAction> actions)
    {
        _service = service;
        _actions = actions;
    }

    public ObservableCollection<PaletteItem> Results { get; } = [];

    /// <summary>选中任务后的跳转动作，由外壳注入（面板不直接依赖任务页）。</summary>
    public Action<TaskItem>? OpenTaskRequested { get; set; }

    public Action<string>? OpenProjectRequested { get; set; }

    [ObservableProperty]
    private bool _isOpen;

    [ObservableProperty]
    private string _query = string.Empty;

    [ObservableProperty]
    private PaletteItem? _selectedItem;

    public bool HasResults => Results.Count > 0;

    public bool ShowEmpty => IsOpen && Results.Count == 0;

    partial void OnQueryChanged(string value) => Rebuild();

    partial void OnIsOpenChanged(bool value) => OnPropertyChanged(nameof(ShowEmpty));

    partial void OnSelectedItemChanged(PaletteItem? value)
    {
        foreach (var item in Results)
        {
            item.IsSelected = ReferenceEquals(item, value);
        }
    }

    public void Open()
    {
        Query = string.Empty;
        IsOpen = true;
        Rebuild();
    }

    [RelayCommand]
    public void Close() => IsOpen = false;

    /// <summary>上下移动选中项。视图层拿到方向键后调用，避免在 XAML 里重复实现键盘逻辑。</summary>
    public void MoveSelection(int delta)
    {
        if (Results.Count == 0) return;

        var current = SelectedItem is null ? -1 : Results.IndexOf(SelectedItem);
        var next = current < 0
            ? (delta > 0 ? 0 : Results.Count - 1)
            : (current + delta + Results.Count) % Results.Count;

        SelectedItem = Results[next];
    }

    [RelayCommand]
    public void RunSelected()
    {
        var item = SelectedItem ?? Results.FirstOrDefault();
        if (item is null) return;

        // 先关面板再执行：动作可能切页或打开抽屉，遮罩留着会挡住结果
        Close();
        item.Invoke();
    }

    private void Rebuild()
    {
        var query = Query.Trim();
        Results.Clear();

        foreach (var action in _actions.Where(a => query.Length == 0 || a.Matches(query)))
        {
            var captured = action;
            Results.Add(new PaletteItem(
                captured.Title,
                captured.Hint,
                captured.Glyph,
                () => captured.Invoke(),
                Activate));
        }

        AddProjectResults(query);
        AddTaskResults(query);

        SelectedItem = Results.FirstOrDefault();
        OnPropertyChanged(nameof(HasResults));
        OnPropertyChanged(nameof(ShowEmpty));
    }

    private void AddProjectResults(string query)
    {
        var projects = _service.ProjectsList().Data ?? [];
        var matches = projects
            .Where(p => query.Length == 0 || p.Name.Contains(query, StringComparison.OrdinalIgnoreCase))
            .Take(query.Length == 0 ? 0 : MaxProjects);

        foreach (var project in matches)
        {
            var captured = project;
            Results.Add(new PaletteItem(
                captured.Name,
                "项目",
                "项",
                () => OpenProjectRequested?.Invoke(captured.Id),
                Activate));
        }
    }

    private void AddTaskResults(string query)
    {
        var filter = new TaskFilter { IncludeDone = false };

        if (query.Length > 0)
        {
            filter = filter with { Search = query };
        }

        var tasks = (_service.TasksList(filter).Data ?? [])
            .OrderBy(t => string.IsNullOrEmpty(t.DueAt) ? 1 : 0)
            .ThenBy(t => t.DueAt, StringComparer.Ordinal)
            .Take(query.Length == 0 ? 6 : MaxTasks);

        var today = LocalTime.ToLocalDate(DateTime.Now);

        foreach (var task in tasks)
        {
            var captured = task;
            var due = task.DueAt;
            var hint = due is null
                ? (task.Status == TaskStatus.Doing ? "进行中" : "任务")
                : due.StartsWith(today, StringComparison.Ordinal)
                    ? "今天"
                    : $"任务 · {Formatting.FormatLocalDateTime(due)}";

            Results.Add(new PaletteItem(
                captured.Title,
                hint,
                "任",
                () => OpenTaskRequested?.Invoke(captured),
                Activate));
        }
    }

    private void Activate(PaletteItem item)
    {
        SelectedItem = item;
        RunSelected();
    }
}
