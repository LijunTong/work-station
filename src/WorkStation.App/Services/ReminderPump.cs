using WorkStation.Core;
using WorkStation.Core.Models;
using WorkStation.Core.Reminders;
using WorkStation.Core.Services;
using WorkStation.Core.Time;

namespace WorkStation.App.Services;

/// <summary>
/// 提醒调度的一次「节拍」。节拍本身由外壳的 <see cref="Avalonia.Threading.DispatcherTimer"/> 驱动，
/// 这里只管发生了什么 —— 时钟与通知出口都可注入，跨日、重复、发送失败才能被测试覆盖。
///
/// 顺序刻意如此：<b>先落状态、再发通知</b>。反过来的话，
/// 通知弹出瞬间崩溃会留下一条仍是 pending 的记录，下次启动又弹一遍。
/// </summary>
public sealed class ReminderPump
{
    private readonly WorkStationService _service;
    private readonly Func<DateTime> _clock;

    public ReminderPump(WorkStationService service, INotificationSink? sink = null, Func<DateTime>? clock = null)
    {
        _service = service;
        Sink = sink;
        _clock = clock ?? (() => DateTime.Now);
    }

    public INotificationSink? Sink { get; set; }

    /// <summary>点提醒通知：定位到对应任务 / 提醒页。</summary>
    public Action<ReminderItem>? OpenReminder { get; set; }

    /// <summary>点站会通知：看板 + 今日筛选。</summary>
    public Action? OpenDailyReview { get; set; }

    public ReminderPumpResult Tick()
    {
        var now = _clock();
        var nowLocal = LocalTime.ToLocalDateTime(now);

        var fired = FireDueReminders(nowLocal);
        var reviewFired = FireDailyReview(nowLocal);

        return new ReminderPumpResult(fired, reviewFired);
    }

    private IReadOnlyList<ReminderItem> FireDueReminders(string nowLocal)
    {
        var fired = new List<ReminderItem>();

        var due = _service.RemindersDue(nowLocal);
        if (!due.Ok || due.Data is not { } items) return fired;

        foreach (var item in items)
        {
            // 任务已经完成或取消：提醒悄悄收起来，不用打扰用户
            if (item.IsTaskClosed)
            {
                _service.RemindersSetStatus(item.Id, ReminderStatus.Dismissed);
                continue;
            }

            if (item.RepeatRule != RepeatRule.None
                && ReminderPlanner.NextOccurrence(item.RemindAt, item.RepeatRule, nowLocal) is { } next)
            {
                // 重复提醒不写 fired：它没有「处理」这一步，直接排下一次
                _service.RemindersReschedule(item.Id, next);
            }
            else
            {
                _service.RemindersSetStatus(item.Id, ReminderStatus.Fired);
            }

            try
            {
                Sink?.Show(TitleOf(item), MessageOf(item), () => OpenReminder?.Invoke(item));
                fired.Add(item);
            }
            catch (Exception)
            {
                // 通知发不出去只影响这一条：标 failed 让用户在提醒页看到，不往外抛
                _service.RemindersSetStatus(item.Id, ReminderStatus.Failed);
            }
        }

        return fired;
    }

    private bool FireDailyReview(string nowLocal)
    {
        var settings = _service.DailyReviewGet();
        if (!settings.Ok || settings.Data is not { } config) return false;

        var lastFired = _service.DailyReviewLastFired();
        if (!DailyReviewPlanner.ShouldFire(config, nowLocal, lastFired.Data)) return false;

        // 先记「今天发过」：站会没有落表记录，这条设置就是它的去重依据
        _service.DailyReviewMarkFired(nowLocal[..10]);

        var counts = _service.DailyReviewCounts(nowLocal);
        var message = counts is { Ok: true, Data: { } data } ? data.Message : "开始今天的站会吧";

        try
        {
            Sink?.Show("今日站会", message, () => OpenDailyReview?.Invoke());
        }
        catch (Exception)
        {
            // 站会提醒没有历史记录，发送失败不记状态，明天照常
        }

        return true;
    }

    private static string TitleOf(ReminderItem item) =>
        item.Kind == ReminderKind.TaskDue ? "任务提醒" : "提醒";

    private static string MessageOf(ReminderItem item)
    {
        if (item.Kind != ReminderKind.TaskDue) return item.Title;

        var parts = new List<string>(2);
        if (!string.IsNullOrWhiteSpace(item.ProjectName)) parts.Add(item.ProjectName!);
        if (item.TaskDueAt is { } due) parts.Add($"截止 {Formatting.FormatLocalDateTime(due)}");

        return parts.Count == 0 ? item.Title : $"{item.Title} · {string.Join(" · ", parts)}";
    }
}

/// <summary>一次节拍的结果：弹了哪些提醒、站会是否触发。测试与徽标刷新都靠它。</summary>
public sealed record ReminderPumpResult(IReadOnlyList<ReminderItem> Fired, bool DailyReviewFired)
{
    public bool Any => Fired.Count > 0 || DailyReviewFired;
}
