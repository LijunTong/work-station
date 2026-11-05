using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WorkStation.Core;
using WorkStation.Core.Models;
using WorkStation.Core.Reminders;
using WorkStation.Core.Services;
using WorkStation.Core.Time;

namespace WorkStation.App.ViewModels;

/// <summary>
/// 提醒页：只有「待办」和「已完成」两组。
///
/// 早先按 pending / fired / handled 分三组，用户得先分清「待触发」和「待处理」才算看得懂；
/// 对提醒这件事，真正有意义的只有「还欠着」和「已经过去」两种。已到点的那几条仍然排在最前面、
/// 带暖底，但不再占一个独立分组。已完成的默认收起 —— 它是存档，不是待办。
/// </summary>
public partial class RemindersViewModel : ObservableObject
{
    private readonly WorkStationService _service;
    private readonly Action<string, bool> _toast;
    private readonly Func<DateTime> _clock;
    private readonly Action? _dataChanged;

    private bool _isReloading;
    private bool _reloadPending;

    public RemindersViewModel(
        WorkStationService service,
        Action<string, bool> toast,
        Func<DateTime>? clock = null,
        Action? dataChanged = null)
    {
        _service = service;
        _toast = toast;
        _clock = clock ?? (() => DateTime.Now);
        _dataChanged = dataChanged;
    }

    /// <summary>还没处理的：已到点的排在最前，其余按时间先后。</summary>
    public ObservableCollection<ReminderRowViewModel> Active { get; } = [];

    public ObservableCollection<ReminderRowViewModel> Handled { get; } = [];

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string? _error;

    [ObservableProperty]
    private bool _isFormOpen;

    [ObservableProperty]
    private ReminderFormViewModel? _form;

    [ObservableProperty]
    private ReminderRowViewModel? _pendingDelete;

    /// <summary>「已完成」默认收起：它是存档，一进页面就该看到还欠着什么。</summary>
    [ObservableProperty]
    private bool _isHandledExpanded;

    public bool IsDeleteConfirmOpen => PendingDelete is not null;

    public bool HasError => Error is not null;

    public bool HasActive => Active.Count > 0;

    public bool HasHandled => Handled.Count > 0;

    public bool ShowEmpty => !IsLoading && Error is null && Active.Count == 0 && Handled.Count == 0;

    public string ActiveCountText => Active.Count.ToString(CultureInfo.InvariantCulture);

    public string HandledCountText => Handled.Count.ToString(CultureInfo.InvariantCulture);

    public string HandledToggleText => IsHandledExpanded ? "收起" : "展开";

    partial void OnErrorChanged(string? value) => OnPropertyChanged(nameof(HasError));

    partial void OnIsLoadingChanged(bool value) => OnPropertyChanged(nameof(ShowEmpty));

    partial void OnPendingDeleteChanged(ReminderRowViewModel? value) =>
        OnPropertyChanged(nameof(IsDeleteConfirmOpen));

    partial void OnIsHandledExpandedChanged(bool value) =>
        OnPropertyChanged(nameof(HandledToggleText));

    public void Reload()
    {
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
            var now = _clock();
            var triggered = _service.RemindersList(ReminderScope.Triggered);
            var upcoming = _service.RemindersList(ReminderScope.Upcoming);
            var handled = _service.RemindersList(ReminderScope.Handled);

            var failure = new[] { triggered, upcoming, handled }.FirstOrDefault(r => !r.Ok);
            if (failure is not null)
            {
                Error = failure.Error?.Message ?? "提醒加载失败";
                return;
            }

            Error = null;
            // 已到点的排前面：那是「现在就欠着」的事，未到点的只是日程
            Fill(Active, [.. triggered.Data ?? [], .. upcoming.Data ?? []], now);
            Fill(Handled, handled.Data ?? [], now);
        }
        finally
        {
            IsLoading = false;
            SyncGroupFlags();
        }
    }

    private void Fill(ObservableCollection<ReminderRowViewModel> target, IReadOnlyList<ReminderItem> items, DateTime now)
    {
        target.Clear();
        foreach (var item in items)
        {
            target.Add(new ReminderRowViewModel(item, now, Complete, Snooze, AskDelete));
        }
    }

    private void SyncGroupFlags()
    {
        OnPropertyChanged(nameof(HasActive));
        OnPropertyChanged(nameof(HasHandled));
        OnPropertyChanged(nameof(ShowEmpty));
        OnPropertyChanged(nameof(ActiveCountText));
        OnPropertyChanged(nameof(HandledCountText));
    }

    [RelayCommand]
    private void NewReminder()
    {
        Form = ReminderFormViewModel.ForCreate(_clock(), SaveForm, CancelForm);
        IsFormOpen = true;
    }

    [RelayCommand]
    private void CancelForm()
    {
        IsFormOpen = false;
        Form = null;
    }

    private void SaveForm()
    {
        if (Form is not { } form) return;
        if (!form.Validate()) return;

        var result = _service.RemindersCreate(form.Build());
        if (!result.Ok)
        {
            form.Error = result.Error?.Message ?? "提醒保存失败";
            return;
        }

        IsFormOpen = false;
        Form = null;
        _toast("提醒已排上", false);
        Reload();
        _dataChanged?.Invoke();
    }

    private void Complete(ReminderRowViewModel row)
    {
        SetStatus(row, ReminderStatus.Done, "已标记完成");
    }

    [RelayCommand]
    private void ToggleHandled() => IsHandledExpanded = !IsHandledExpanded;

    /// <summary>稍后提醒：固定推后 10 分钟，回到待触发。比「再想一个时间」快得多。</summary>
    private void Snooze(ReminderRowViewModel row)
    {
        var next = LocalTime.ToLocalDateTime(_clock().AddMinutes(10));
        var result = _service.RemindersReschedule(row.Id, next);

        if (!result.Ok)
        {
            _toast(result.Error?.Message ?? "稍后提醒设置失败", true);
            return;
        }

        _toast("10 分钟后再提醒", false);
        Reload();
        _dataChanged?.Invoke();
    }

    private void SetStatus(ReminderRowViewModel row, ReminderStatus status, string message)
    {
        var result = _service.RemindersSetStatus(row.Id, status);
        if (!result.Ok)
        {
            _toast(result.Error?.Message ?? "提醒更新失败", true);
            return;
        }

        _toast(message, false);
        Reload();
        _dataChanged?.Invoke();
    }

    private void AskDelete(ReminderRowViewModel row) => PendingDelete = row;

    [RelayCommand]
    private void CancelDelete() => PendingDelete = null;

    [RelayCommand]
    private void ConfirmDelete()
    {
        if (PendingDelete is not { } row) return;

        PendingDelete = null;
        var result = _service.RemindersRemove(row.Id);
        if (!result.Ok)
        {
            _toast(result.Error?.Message ?? "删除提醒失败", true);
            return;
        }

        _toast("提醒已删除", false);
        Reload();
        _dataChanged?.Invoke();
    }
}

/// <summary>提醒列表里的一行。</summary>
public sealed partial class ReminderRowViewModel : ObservableObject
{
    private readonly Action<ReminderRowViewModel> _complete;
    private readonly Action<ReminderRowViewModel> _snooze;
    private readonly Action<ReminderRowViewModel> _delete;

    public ReminderRowViewModel(
        ReminderItem item,
        DateTime now,
        Action<ReminderRowViewModel> complete,
        Action<ReminderRowViewModel> snooze,
        Action<ReminderRowViewModel> delete)
    {
        Item = item;
        _complete = complete;
        _snooze = snooze;
        _delete = delete;
        TimeText = FormatTime(item.RemindAt, now);
        IsOverdue = item.Status == ReminderStatus.Pending && Formatting.IsOverdue(item.RemindAt, LocalTime.ToLocalDateTime(now));
    }

    public ReminderItem Item { get; }

    public string Id => Item.Id;

    public string Title => Item.Title;

    public string? ProjectName => Item.ProjectName;

    public bool HasProject => !string.IsNullOrWhiteSpace(ProjectName);

    public string TimeText { get; }

    /// <summary>已经过了提醒时间但还没处理：时间文字转琥珀色，行也带暖底。</summary>
    public bool IsOverdue { get; }

    public bool IsTaskReminder => Item.Kind == ReminderKind.TaskDue;

    public string SourceText => IsTaskReminder ? "任务截止" : "自定义";

    public string RepeatText => ReminderPlanner.DescribeRepeat(Item.RepeatRule);

    public bool HasRepeat => Item.RepeatRule != RepeatRule.None;

    /// <summary>已经弹过（到点了）：只有这种才给「10 分钟后再提醒」，没到点的本来就还会响。</summary>
    public bool IsTriggered => Item.Status == ReminderStatus.Fired;

    public bool IsHandled => Item.Status is ReminderStatus.Done or ReminderStatus.Dismissed or ReminderStatus.Failed;

    /// <summary>未处理的行才给「完成」；已完成的行只留删除。</summary>
    public bool IsActionable => !IsHandled;

    /// <summary>元信息一行：来源 · 项目 · 重复。用中点拼好，避免 XAML 里堆分隔符。</summary>
    public string MetaText
    {
        get
        {
            var parts = new List<string>(3) { SourceText };
            if (HasProject) parts.Add(ProjectName!);
            if (HasRepeat) parts.Add(RepeatText);
            return string.Join(" · ", parts);
        }
    }

    [RelayCommand]
    private void Complete() => _complete(this);

    [RelayCommand]
    private void Snooze() => _snooze(this);

    [RelayCommand]
    private void Delete() => _delete(this);

    /// <summary>
    /// 「今天 18:00」「明天 09:00」「昨天 21:30」，更远的用「11-07 09:00」。
    /// 跨年补年份：否则明年的 1 月 5 日会显示成今年的。
    /// </summary>
    public static string FormatTime(string remindAt, DateTime now)
    {
        if (remindAt.Length < 16
            || !DateTime.TryParseExact(remindAt, "yyyy-MM-dd'T'HH:mm", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var at))
        {
            return remindAt;
        }

        var time = $"{at.Hour:00}:{at.Minute:00}";
        var days = (at.Date - now.Date).Days;

        return days switch
        {
            0 => $"今天 {time}",
            1 => $"明天 {time}",
            -1 => $"昨天 {time}",
            > 1 and < 7 => $"周{WeekdayName(at)} {time}",
            _ when at.Year != now.Year => $"{at.Year}-{at.Month:00}-{at.Day:00} {time}",
            _ => $"{at.Month:00}-{at.Day:00} {time}"
        };
    }

    private static string WeekdayName(DateTime value) =>
        new[] { "日", "一", "二", "三", "四", "五", "六" }[(int)value.DayOfWeek];
}

/// <summary>新建自定义提醒的表单。</summary>
public partial class ReminderFormViewModel : ObservableObject
{
    private readonly Action _save;
    private readonly Action _cancel;

    public static readonly IReadOnlyList<string> RepeatOptions = ["不重复", "每天", "每周"];

    [ObservableProperty]
    private string _title = string.Empty;

    [ObservableProperty]
    private DateTime? _date;

    [ObservableProperty]
    private TimeSpan? _time;

    [ObservableProperty]
    private int _repeatIndex;

    [ObservableProperty]
    private string? _error;

    private ReminderFormViewModel(Action save, Action cancel)
    {
        _save = save;
        _cancel = cancel;
    }

    public string TitleText => "新建提醒";

    public static ReminderFormViewModel ForCreate(DateTime now, Action save, Action cancel)
    {
        // 默认给「半小时后的整 5 分钟」：多数提醒不是马上要，而是过一会儿
        var suggested = now.AddMinutes(30);
        var rounded = new DateTime(suggested.Year, suggested.Month, suggested.Day,
            suggested.Hour, suggested.Minute / 5 * 5, 0);

        return new ReminderFormViewModel(save, cancel)
        {
            Date = rounded.Date,
            Time = rounded.TimeOfDay
        };
    }

    [RelayCommand]
    private void Save() => _save();

    [RelayCommand]
    private void Cancel() => _cancel();

    public bool Validate()
    {
        if (string.IsNullOrWhiteSpace(Title))
        {
            Error = "提醒内容不能为空";
            return false;
        }

        if (Date is null)
        {
            Error = "选一个提醒时间";
            return false;
        }

        Error = null;
        return true;
    }

    public CreateReminderInput Build() => new()
    {
        Title = Title.Trim(),
        RemindAt = LocalTime.ToLocalDateTime((Date ?? DateTime.Today).Date.Add(Time ?? new TimeSpan(9, 0, 0))),
        RepeatRule = RepeatIndex switch
        {
            1 => RepeatRule.Daily,
            2 => RepeatRule.Weekly,
            _ => RepeatRule.None
        }
    };
}
