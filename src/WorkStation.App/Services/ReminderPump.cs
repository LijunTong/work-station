using WorkStation.Core;
using WorkStation.Core.Models;
using WorkStation.Core.Reminders;
using WorkStation.Core.Reports;
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

    /// <summary>点报告提醒：进报告页的对应类型。</summary>
    public Action<ReportKind>? OpenReport { get; set; }

    public ReminderPumpResult Tick()
    {
        var now = _clock();
        var nowLocal = LocalTime.ToLocalDateTime(now);

        var fired = FireDueReminders(nowLocal);
        var reportFired = FireReportReminders(now);

        return new ReminderPumpResult(fired, reportFired);
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

    /// <summary>
    /// 三类报告提醒：日报 / 周报 / 月报。
    ///
    /// **先记去重键、再发通知** —— 跟任务提醒同一个道理：
    /// 通知发出瞬间崩溃了，下次不该把同一个周期再报一遍。
    /// </summary>
    private IReadOnlyList<ReportKind> FireReportReminders(DateTime now)
    {
        var fired = new List<ReportKind>();

        var settings = _service.ReportRemindersGet();
        if (!settings.Ok || settings.Data is not { } config) return fired;

        foreach (var kind in new[] { ReportKind.Daily, ReportKind.Weekly, ReportKind.Monthly })
        {
            var entry = kind switch
            {
                ReportKind.Daily => config.Daily,
                ReportKind.Weekly => config.Weekly,
                _ => config.Monthly
            };

            var lastFired = _service.ReportReminderLastFired(kind);
            if (!ReportReminderPlanner.ShouldFire(kind, entry, now, lastFired.Data)) continue;

            var key = ReportReminderPlanner.PeriodKey(kind, now);
            _service.ReportReminderMarkFired(kind, key);

            try
            {
                var stats = _service.ReportsStats(kind);
                var message = stats is { Ok: true, Data: { } data }
                    ? $"完成 {data.Completed} 项 · 逾期 {data.Overdue} 项"
                    : "该写报告了";

                Sink?.Show(TitleOf(kind), message, () => OpenReport?.Invoke(kind));
                fired.Add(kind);
            }
            catch (Exception)
            {
                // 去重键按「先落状态」的约定保留：通知没发出去也不在这一周期重弹。
                // 报告提醒没有历史记录，所以不像任务提醒那样还能落一条 Failed 给用户看。
            }
        }

        return fired;
    }

    private static string TitleOf(ReportKind kind) => kind switch
    {
        ReportKind.Daily => "该写今天的日报了",
        ReportKind.Weekly => "该写本周周报了",
        _ => "该写本月月报了"
    };

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

/// <summary>一次节拍的结果：弹了哪些提醒、哪些报告提醒触发了。测试与徽标刷新都靠它。</summary>
public sealed record ReminderPumpResult(
    IReadOnlyList<ReminderItem> Fired,
    IReadOnlyList<ReportKind> ReportRemindersFired)
{
    public bool Any => Fired.Count > 0 || ReportRemindersFired.Count > 0;
}
