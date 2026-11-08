using Microsoft.Data.Sqlite;
using WorkStation.App.Services;
using WorkStation.Core.Models;
using WorkStation.Core.Services;
using WorkStation.Core.Time;

namespace WorkStation.Tests;

/// <summary>
/// 提醒调度的一拍：该弹的弹、弹过的不再弹、重复的排下一次、发不出去只标 failed。
/// 时钟注入固定值，跨日行为才能被真正断言。
/// </summary>
public class ReminderPumpTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly WorkStationService _service;
    private readonly FakeSink _sink = new();

    public ReminderPumpTests()
    {
        _connection = TestDb.OpenMigrated();
        _service = new WorkStationService(_connection, ":memory:");

        // 三类报告提醒默认都开着，会让每个用例都多出一条通知，这里先全部关掉，需要它的用例自己打开
        _service.ReportRemindersSet(new ReportReminderSettings
        {
            Daily = new ReportReminderEntry { Enabled = false, Time = "09:00" },
            Weekly = new ReportReminderEntry { Enabled = false, Time = "17:00", DayOfWeek = 5 },
            Monthly = new ReportReminderEntry { Enabled = false, Time = "17:00", DayOfMonth = 31 }
        });
    }

    public void Dispose() => _connection.Dispose();

    private static DateTime At(int day, int hour, int minute = 0) =>
        new(2026, 11, day, hour, minute, 0, DateTimeKind.Local);

    private ReminderPump PumpAt(DateTime now) => new(_service, _sink, () => now);

    private static string Local(DateTime value) => LocalTime.ToLocalDateTime(value);

    [Fact]
    public void Tick_FiresDueReminderAndMarksFired()
    {
        _service.RemindersCreate(new CreateReminderInput { Title = "喝水", RemindAt = Local(At(5, 11)) });

        var pump = PumpAt(At(5, 12));
        var result = pump.Tick();

        var fired = Assert.Single(result.Fired);
        Assert.Equal("喝水", fired.Title);
        Assert.Single(_sink.Shown);
        Assert.Equal("提醒", _sink.Shown[0].Title);
        Assert.Contains("喝水", _sink.Shown[0].Message);

        // 再跑一拍：已经是 fired，不该重复弹
        Assert.Empty(pump.Tick().Fired);
        Assert.Single(_sink.Shown);
        Assert.Single(_service.RemindersList(ReminderScope.Triggered).Data!);
    }

    [Fact]
    public void Tick_LeavesFutureReminderAlone()
    {
        _service.RemindersCreate(new CreateReminderInput { Title = "明天", RemindAt = Local(At(6, 9)) });

        var result = PumpAt(At(5, 12)).Tick();

        Assert.Empty(result.Fired);
        Assert.Empty(_sink.Shown);
        Assert.Single(_service.RemindersList(ReminderScope.Upcoming).Data!);
    }

    [Fact]
    public void Tick_TaskReminderCarriesProjectAndDueTime()
    {
        var project = _service.ProjectsCreate(new CreateProjectInput { Name = "年度总结" }).Data!;
        var task = _service.TasksCreate(new CreateTaskInput
        {
            Title = "写总结",
            ProjectId = project.Id,
            DueAt = Local(At(5, 18)),
            RemindBefore = 60
        }).Data!;

        var result = PumpAt(At(5, 17, 30)).Tick();

        var fired = Assert.Single(result.Fired);
        Assert.Equal(task.Id, fired.TaskId);
        Assert.Equal("任务提醒", _sink.Shown[0].Title);
        Assert.Contains("写总结", _sink.Shown[0].Message);
        Assert.Contains("年度总结", _sink.Shown[0].Message);
        Assert.Contains("11-05 18:00", _sink.Shown[0].Message);
    }

    [Fact]
    public void Tick_RepeatReminderIsRescheduledInsteadOfFired()
    {
        var created = _service.RemindersCreate(new CreateReminderInput
        {
            Title = "每天喝水",
            RemindAt = Local(At(5, 11)),
            RepeatRule = RepeatRule.Daily
        }).Data!;

        var result = PumpAt(At(5, 12)).Tick();

        Assert.Single(result.Fired);
        Assert.Single(_sink.Shown);

        var item = _service.RemindersGet(created.Id).Data!;
        Assert.Equal(ReminderStatus.Pending, item.Status);
        Assert.Equal(Local(At(6, 11)), item.RemindAt);

        // 同一天再跑：下一次在明天，不会再弹
        Assert.Empty(PumpAt(At(5, 18)).Tick().Fired);
    }

    [Fact]
    public void Tick_CompletedTaskReminderIsSilentlyDismissed()
    {
        var task = _service.TasksCreate(new CreateTaskInput
        {
            Title = "会被完成",
            DueAt = Local(At(5, 18)),
            RemindBefore = 60
        }).Data!;

        // 模拟「提醒已经排上、任务随后被完成」的窗口期
        _service.TasksSetStatus(task.Id, TaskStatus.Done);
        TestDb.Execute(_connection, $"""
            INSERT INTO reminders (id, kind, task_id, title, remind_at, repeat_rule, channel, status, created_at, updated_at)
            VALUES ('r-done', 'task_due', '{task.Id}', '会被完成', '{Local(At(5, 17))}', 'none', 'system', 'pending',
                    '{LocalTime.NowIso()}', '{LocalTime.NowIso()}')
            """);

        var result = PumpAt(At(5, 17, 30)).Tick();

        Assert.Empty(result.Fired);
        Assert.Empty(_sink.Shown);
        var item = _service.RemindersGet("r-done").Data!;
        Assert.Equal(ReminderStatus.Dismissed, item.Status);
    }

    [Fact]
    public void Tick_SinkFailure_MarksReminderFailed()
    {
        _sink.Throws = true;
        _service.RemindersCreate(new CreateReminderInput { Title = "弹不出来", RemindAt = Local(At(5, 11)) });

        PumpAt(At(5, 12)).Tick();

        var handled = Assert.Single(_service.RemindersList(ReminderScope.Handled).Data!);
        Assert.Equal(ReminderStatus.Failed, handled.Status);
    }

    [Fact]
    public void Tick_ReportReminderFiresOncePerDay()
    {
        _service.ReportRemindersSet(new ReportReminderSettings
        {
            Daily = new ReportReminderEntry { Enabled = true, Time = "09:00" },
            Weekly = new ReportReminderEntry { Enabled = false, Time = "17:00", DayOfWeek = 5 },
            Monthly = new ReportReminderEntry { Enabled = false, Time = "17:00", DayOfMonth = 31 }
        });

        var pump = PumpAt(At(5, 9, 5));
        var first = pump.Tick();

        Assert.Contains(ReportKind.Daily, first.ReportRemindersFired);
        Assert.Contains(_sink.Shown, n => n.Title == "该写今天的日报了");

        // 同一周期（已经是今天）不再发
        Assert.DoesNotContain(ReportKind.Daily, pump.Tick().ReportRemindersFired);

        // 第二天到点：照常发
        Assert.Contains(ReportKind.Daily, PumpAt(At(6, 9, 1)).Tick().ReportRemindersFired);
    }

    [Fact]
    public void Tick_ReportReminderBeforeTimeOrDisabled_DoesNotFire()
    {
        _service.ReportRemindersSet(new ReportReminderSettings
        {
            Daily = new ReportReminderEntry { Enabled = true, Time = "09:00" },
            Weekly = new ReportReminderEntry { Enabled = false, Time = "17:00", DayOfWeek = 5 },
            Monthly = new ReportReminderEntry { Enabled = false, Time = "17:00", DayOfMonth = 31 }
        });

        Assert.DoesNotContain(ReportKind.Daily, PumpAt(At(5, 8, 59)).Tick().ReportRemindersFired);

        _service.ReportRemindersSet(new ReportReminderSettings
        {
            Daily = new ReportReminderEntry { Enabled = false, Time = "09:00" },
            Weekly = new ReportReminderEntry { Enabled = false, Time = "17:00", DayOfWeek = 5 },
            Monthly = new ReportReminderEntry { Enabled = false, Time = "17:00", DayOfMonth = 31 }
        });

        Assert.DoesNotContain(ReportKind.Daily, PumpAt(At(5, 10)).Tick().ReportRemindersFired);
    }

    [Fact]
    public void Tick_ReportReminderClickOpensThatReport()
    {
        _service.ReportRemindersSet(new ReportReminderSettings
        {
            Daily = new ReportReminderEntry { Enabled = true, Time = "09:00" },
            Weekly = new ReportReminderEntry { Enabled = false, Time = "17:00", DayOfWeek = 5 },
            Monthly = new ReportReminderEntry { Enabled = false, Time = "17:00", DayOfMonth = 31 }
        });

        ReportKind? opened = null;
        var pump = PumpAt(At(5, 9, 5));
        pump.OpenReport = kind => opened = kind;
        pump.Tick();

        _sink.Shown.First(n => n.Title == "该写今天的日报了").Click();

        Assert.Equal(ReportKind.Daily, opened);
    }

    [Fact]
    public void Tick_WeeklyReportReminderFiresOnConfiguredWeekday()
    {
        // 2026-11-06 是周五
        _service.ReportRemindersSet(new ReportReminderSettings
        {
            Daily = new ReportReminderEntry { Enabled = false, Time = "09:00" },
            Weekly = new ReportReminderEntry { Enabled = true, Time = "17:00", DayOfWeek = 5 },
            Monthly = new ReportReminderEntry { Enabled = false, Time = "17:00", DayOfMonth = 31 }
        });

        Assert.DoesNotContain(ReportKind.Weekly, PumpAt(At(5, 17, 0)).Tick().ReportRemindersFired);
        Assert.Contains(ReportKind.Weekly, PumpAt(At(6, 17, 0)).Tick().ReportRemindersFired);
        Assert.Contains(_sink.Shown, n => n.Title == "该写本周周报了");
    }

    private sealed class FakeSink : INotificationSink
    {
        public List<ShownNotification> Shown { get; } = [];

        public bool Throws { get; set; }

        public void Show(string title, string message, Action onClick)
        {
            if (Throws) throw new InvalidOperationException("通知宿主不可用");
            Shown.Add(new ShownNotification(title, message, onClick));
        }
    }

    private sealed record ShownNotification(string Title, string Message, Action Click);
}
