using System.Collections.ObjectModel;
using System.Globalization;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WorkStation.Core;
using WorkStation.Core.Models;
using WorkStation.Core.Services;

namespace WorkStation.App.ViewModels;

/// <summary>项目页。卡片列表 + 新建/编辑抽屉 + 归档与删除。</summary>
public partial class ProjectsViewModel : ObservableObject
{
    private readonly WorkStationService _service;
    private readonly Action<string, bool> _toast;

    public ProjectsViewModel(WorkStationService service, Action<string, bool> toast)
    {
        _service = service;
        _toast = toast;
    }

    public ObservableCollection<ProjectCardViewModel> Projects { get; } = [];

    [ObservableProperty]
    private bool _includeArchived;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string? _error;

    [ObservableProperty]
    private bool _isFormOpen;

    [ObservableProperty]
    private ProjectFormViewModel? _form;

    /// <summary>等待二次确认删除的项目。</summary>
    [ObservableProperty]
    private ProjectCardViewModel? _pendingDelete;

    public bool HasProjects => Projects.Count > 0;

    /// <summary>空状态只在「加载完成、没有错误、确实没数据」时出现 —— 否则会和错误提示同屏。</summary>
    public bool ShowEmpty => !IsLoading && Error is null && Projects.Count == 0;

    public bool HasError => Error is not null;

    public bool IsDeleteConfirmOpen => PendingDelete is not null;

    partial void OnIncludeArchivedChanged(bool value) => Reload();

    partial void OnErrorChanged(string? value)
    {
        OnPropertyChanged(nameof(HasError));
        OnPropertyChanged(nameof(ShowEmpty));
    }

    partial void OnIsLoadingChanged(bool value) => OnPropertyChanged(nameof(ShowEmpty));

    partial void OnPendingDeleteChanged(ProjectCardViewModel? value) =>
        OnPropertyChanged(nameof(IsDeleteConfirmOpen));

    public void Reload()
    {
        IsLoading = true;
        try
        {
            var projects = _service.ProjectsList(IncludeArchived);
            if (!projects.Ok || projects.Data is null)
            {
                Error = projects.Error?.Message ?? "项目加载失败";
                Projects.Clear();
                return;
            }

            // 任务只查一次，按项目分组后算进度 —— 避免每个项目各查一遍的 N+1
            var tasks = _service.TasksList();
            var tasksByProject = (tasks.Data ?? [])
                .Where(t => t.ProjectId is not null)
                .GroupBy(t => t.ProjectId!)
                .ToDictionary(g => g.Key, g => g.ToList());

            if (!tasks.Ok)
            {
                Error = tasks.Error?.Message ?? "任务加载失败，项目进度可能不准确";
            }
            else
            {
                Error = null;
            }

            Projects.Clear();
            foreach (var project in projects.Data)
            {
                tasksByProject.TryGetValue(project.Id, out var projectTasks);
                Projects.Add(ProjectCardViewModel.Create(
                    project,
                    projectTasks ?? [],
                    EditProject,
                    ArchiveProject,
                    RequestDelete));
            }
        }
        finally
        {
            IsLoading = false;
            OnPropertyChanged(nameof(HasProjects));
            OnPropertyChanged(nameof(ShowEmpty));
        }
    }

    [RelayCommand]
    private void NewProject()
    {
        Form = ProjectFormViewModel.ForCreate(SaveForm, CloseForm);
        IsFormOpen = true;
    }

    [RelayCommand]
    private void EditProject(ProjectCardViewModel card)
    {
        Form = ProjectFormViewModel.ForEdit(card.Project, SaveForm, CloseForm);
        IsFormOpen = true;
    }

    [RelayCommand]
    private void CloseForm() => IsFormOpen = false;

    [RelayCommand]
    private void SaveForm()
    {
        if (Form is not { } form) return;
        if (!form.Validate()) return;

        if (form.IsEditing)
        {
            var updated = _service.ProjectsUpdate(form.ProjectId!, form.BuildUpdate());
            if (!updated.Ok)
            {
                form.Error = updated.Error?.Message ?? "保存失败";
                return;
            }
        }
        else
        {
            var created = _service.ProjectsCreate(form.BuildCreate());
            if (!created.Ok)
            {
                form.Error = created.Error?.Message ?? "保存失败";
                return;
            }
        }

        IsFormOpen = false;
        _toast(form.IsEditing ? "项目已更新" : "项目已创建", false);
        Reload();
    }

    [RelayCommand]
    private void ArchiveProject(ProjectCardViewModel card)
    {
        var result = card.IsArchived
            ? _service.ProjectsUnarchive(card.Project.Id)
            : _service.ProjectsArchive(card.Project.Id);

        if (!result.Ok)
        {
            _toast(result.Error?.Message ?? "操作失败", true);
            return;
        }

        _toast(card.IsArchived ? "已取消归档" : "项目已归档", false);
        Reload();
    }

    [RelayCommand]
    private void RequestDelete(ProjectCardViewModel card) => PendingDelete = card;

    [RelayCommand]
    private void CancelDelete() => PendingDelete = null;

    [RelayCommand]
    private void ConfirmDelete()
    {
        if (PendingDelete is not { } card) return;

        var result = _service.ProjectsRemove(card.Project.Id);
        PendingDelete = null;

        if (!result.Ok)
        {
            _toast(result.Error?.Message ?? "删除失败", true);
            return;
        }

        _toast("项目及其任务已删除", false);
        Reload();
    }

    [RelayCommand]
    private void OpenTaskPage()
    {
        // 由 MainWindowViewModel 在构造时挂上，避免子 VM 反向依赖父 VM
        NavigateToTasks?.Invoke();
    }

    public Action? NavigateToTasks { get; set; }
}

/// <summary>项目卡片。进度口径与 ProgressCalculator 一致：auto 排除 canceled，manual 取手动值。</summary>
public sealed partial class ProjectCardViewModel : ObservableObject
{
    private readonly Action<ProjectCardViewModel> _edit;
    private readonly Action<ProjectCardViewModel> _archive;
    private readonly Action<ProjectCardViewModel> _delete;

    private ProjectCardViewModel(
        Project project,
        TaskCounts counts,
        int progress,
        Action<ProjectCardViewModel> edit,
        Action<ProjectCardViewModel> archive,
        Action<ProjectCardViewModel> delete)
    {
        Project = project;
        Counts = counts;
        Progress = progress;
        _edit = edit;
        _archive = archive;
        _delete = delete;
    }

    public Project Project { get; }

    public TaskCounts Counts { get; }

    public int Progress { get; }

    public string Name => Project.Name;

    public string? Description => string.IsNullOrWhiteSpace(Project.Description) ? null : Project.Description;

    public bool HasDescription => Description is not null;

    public bool HasColor => !string.IsNullOrWhiteSpace(Project.Color);

    public bool IsArchived => Project.ArchivedAt is not null;

    public string ArchiveLabel => IsArchived ? "取消归档" : "归档";

    public string ProgressText => $"{Progress}%";

    public string StatusText => Project.Status switch
    {
        ProjectStatus.Planning => "规划中",
        ProjectStatus.Active => "进行中",
        ProjectStatus.Paused => "已暂停",
        _ => "已完成"
    };

    public string PriorityText => Project.Priority switch
    {
        Priority.High => "高",
        Priority.Medium => "中",
        Priority.Low => "低",
        _ => "无"
    };

    public bool HasPriority => Project.Priority != Priority.None;

    public string MetaText => $"{Counts.Total} 个任务 · 进行中 {Counts.Doing}";

    public string OverdueText => Counts.Overdue > 0 ? $"逾期 {Counts.Overdue}" : string.Empty;

    public bool HasOverdue => Counts.Overdue > 0;

    public string DueText => string.IsNullOrEmpty(Project.DueDate)
        ? "无目标日期"
        : $"目标 {Formatting.FormatLocalDateTime(Project.DueDate)}";

    public bool IsDueSoon => !string.IsNullOrEmpty(Project.DueDate)
                             && string.CompareOrdinal(Project.DueDate, Core.Time.LocalTime.ToLocalDate(DateTime.Now)) < 0
                             && Project.Status != ProjectStatus.Done;

    public IBrush AccentBrush => ParseColor(Project.Color);

    [RelayCommand]
    private void Edit() => _edit(this);

    [RelayCommand]
    private void ToggleArchive() => _archive(this);

    [RelayCommand]
    private void Delete() => _delete(this);

    public static ProjectCardViewModel Create(
        Project project,
        IReadOnlyList<TaskItem> tasks,
        Action<ProjectCardViewModel> edit,
        Action<ProjectCardViewModel> archive,
        Action<ProjectCardViewModel> delete)
    {
        var counts = new TaskCounts
        {
            Total = tasks.Count,
            Done = tasks.Count(t => t.Status == TaskStatus.Done),
            Doing = tasks.Count(t => t.Status == TaskStatus.Doing),
            Overdue = tasks.Count(t => t.Status is not (TaskStatus.Done or TaskStatus.Canceled)
                                       && Formatting.IsOverdue(t.DueAt, Core.Time.LocalTime.ToLocalDateTime(DateTime.Now)))
        };

        var progress = project.ProgressMode == ProgressMode.Manual
            ? Math.Clamp(project.ProgressManual ?? 0, 0, 100)
            : ProgressCalculator.Compute(tasks);

        return new ProjectCardViewModel(project, counts, progress, edit, archive, delete);
    }

    private static IBrush ParseColor(string? color)
    {
        if (string.IsNullOrWhiteSpace(color)) return Brushes.Transparent;
        try
        {
            return Brush.Parse(color);
        }
        catch (FormatException)
        {
            return Brushes.Transparent;
        }
    }
}

/// <summary>新建/编辑项目的表单状态。校验放在这里，错误就地显示，不弹窗。</summary>
public partial class ProjectFormViewModel : ObservableObject
{
    private readonly Action _save;
    private readonly Action _cancel;

    public static readonly IReadOnlyList<string> StatusOptions = ["规划中", "进行中", "已暂停", "已完成"];

    public static readonly IReadOnlyList<string> PriorityOptions = ["无", "低", "中", "高"];

    public static readonly IReadOnlyList<string> ProgressModeOptions = ["按任务自动计算", "手动指定"];

    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    private string _description = string.Empty;

    [ObservableProperty]
    private int _statusIndex;

    [ObservableProperty]
    private int _priorityIndex;

    [ObservableProperty]
    private string _startDate = string.Empty;

    [ObservableProperty]
    private string _dueDate = string.Empty;

    [ObservableProperty]
    private int _progressModeIndex;

    [ObservableProperty]
    private string _progressManual = "0";

    [ObservableProperty]
    private string? _error;

    public string? ProjectId { get; private init; }

    public bool IsEditing => ProjectId is not null;

    public string Title => IsEditing ? "编辑项目" : "新建项目";

    public bool IsManualProgress => ProgressModeIndex == 1;

    partial void OnProgressModeIndexChanged(int value) => OnPropertyChanged(nameof(IsManualProgress));

    private ProjectFormViewModel(Action save, Action cancel)
    {
        _save = save;
        _cancel = cancel;
    }

    [RelayCommand]
    private void Save() => _save();

    [RelayCommand]
    private void Cancel() => _cancel();

    public static ProjectFormViewModel ForCreate(Action save, Action cancel) =>
        new(save, cancel);

    public static ProjectFormViewModel ForEdit(Project project, Action save, Action cancel) => new(save, cancel)
    {
        ProjectId = project.Id,
        Name = project.Name,
        Description = project.Description,
        StatusIndex = project.Status switch
        {
            ProjectStatus.Planning => 0,
            ProjectStatus.Active => 1,
            ProjectStatus.Paused => 2,
            _ => 3
        },
        PriorityIndex = project.Priority switch
        {
            Priority.None => 0,
            Priority.Low => 1,
            Priority.Medium => 2,
            _ => 3
        },
        StartDate = project.StartDate ?? string.Empty,
        DueDate = project.DueDate ?? string.Empty,
        ProgressModeIndex = project.ProgressMode == ProgressMode.Manual ? 1 : 0,
        ProgressManual = (project.ProgressManual ?? 0).ToString(CultureInfo.InvariantCulture)
    };

    public bool Validate()
    {
        if (string.IsNullOrWhiteSpace(Name))
        {
            Error = "项目名称不能为空";
            return false;
        }

        if (!IsDateOrEmpty(StartDate))
        {
            Error = "开始日期格式应为 YYYY-MM-DD";
            return false;
        }

        if (!IsDateOrEmpty(DueDate))
        {
            Error = "目标日期格式应为 YYYY-MM-DD";
            return false;
        }

        if (!string.IsNullOrWhiteSpace(StartDate) && !string.IsNullOrWhiteSpace(DueDate)
            && string.CompareOrdinal(StartDate, DueDate) > 0)
        {
            Error = "目标日期不能早于开始日期";
            return false;
        }

        if (IsManualProgress && !TryParseProgress(out _))
        {
            Error = "手动进度应为 0–100 的整数";
            return false;
        }

        Error = null;
        return true;
    }

    public CreateProjectInput BuildCreate()
    {
        Validate();
        return new CreateProjectInput
        {
            Name = Name.Trim(),
            Description = Description.Trim(),
            Status = ToStatus(StatusIndex),
            Priority = ToPriority(PriorityIndex),
            StartDate = NullIfEmpty(StartDate),
            DueDate = NullIfEmpty(DueDate),
            ProgressMode = ProgressModeIndex == 1 ? ProgressMode.Manual : ProgressMode.Auto,
            ProgressManual = ProgressModeIndex == 1 && TryParseProgress(out var value) ? value : null
        };
    }

    public UpdateProjectInput BuildUpdate()
    {
        Validate();
        return new UpdateProjectInput
        {
            Name = Optional<string>.Some(Name.Trim()),
            Description = Optional<string?>.Some(Description.Trim()),
            Status = Optional<ProjectStatus>.Some(ToStatus(StatusIndex)),
            Priority = Optional<Priority>.Some(ToPriority(PriorityIndex)),
            StartDate = Optional<string?>.Some(NullIfEmpty(StartDate)),
            DueDate = Optional<string?>.Some(NullIfEmpty(DueDate)),
            ProgressMode = Optional<ProgressMode>.Some(ProgressModeIndex == 1 ? ProgressMode.Manual : ProgressMode.Auto),
            ProgressManual = Optional<int?>.Some(ProgressModeIndex == 1 && TryParseProgress(out var value) ? value : null)
        };
    }

    private bool TryParseProgress(out int value) =>
        int.TryParse(ProgressManual, NumberStyles.Integer, CultureInfo.InvariantCulture, out value)
        && value is >= 0 and <= 100;

    private static bool IsDateOrEmpty(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return true;
        return DateOnly.TryParseExact(value.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _);
    }

    private static string? NullIfEmpty(string value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static ProjectStatus ToStatus(int index) => index switch
    {
        1 => ProjectStatus.Active,
        2 => ProjectStatus.Paused,
        3 => ProjectStatus.Done,
        _ => ProjectStatus.Planning
    };

    private static Priority ToPriority(int index) => index switch
    {
        1 => Priority.Low,
        2 => Priority.Medium,
        3 => Priority.High,
        _ => Priority.None
    };
}
