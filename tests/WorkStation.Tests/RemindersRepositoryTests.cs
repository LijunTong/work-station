using Microsoft.Data.Sqlite;
using WorkStation.Core.Data.Repositories;
using WorkStation.Core.Models;
using WorkStation.Core.Time;

namespace WorkStation.Tests;

/// <summary>提醒仓储：三条来源统一成一张表，任务提醒始终跟着任务走。</summary>
public class RemindersRepositoryTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly RemindersRepository _reminders;
    private readonly TasksRepository _tasks;
    private readonly ProjectsRepository _projects;

    public RemindersRepositoryTests()
    {
        _connection = TestDb.OpenMigrated();
        _reminders = new RemindersRepository(_connection);
        _tasks = new TasksRepository(_connection);
        _projects = new ProjectsRepository(_connection);
    }

    public void Dispose() => _connection.Dispose();

    private static string At(int daysFromToday, int hour, int minute = 0) =>
        LocalTime.ToLocalDateTime(DateTime.Today.AddDays(daysFromToday).AddHours(hour).AddMinutes(minute));

    [Fact]
    public void Create_StoresCustomReminderAsPending()
    {
        var item = _reminders.Create(new CreateReminderInput
        {
            Title = "交电费",
            RemindAt = At(1, 9)
        });

        Assert.Equal("交电费", item.Title);
        Assert.Equal(ReminderKind.Custom, item.Kind);
        Assert.Equal(ReminderStatus.Pending, item.Status);
        Assert.Null(item.TaskId);
    }

    [Fact]
    public void Create_EmptyTitle_Throws()
    {
        var error = Assert.Throws<ArgumentException>(() =>
            _reminders.Create(new CreateReminderInput { Title = "  ", RemindAt = At(1, 9) }));

        Assert.Contains("提醒内容不能为空", error.Message);
    }

    [Fact]
    public void Create_InvalidTime_Throws()
    {
        var error = Assert.Throws<ArgumentException>(() =>
            _reminders.Create(new CreateReminderInput { Title = "x", RemindAt = "2026-11-05 09:00" }));

        Assert.Contains("YYYY-MM-DDTHH:mm", error.Message);
    }

    [Fact]
    public void ListDue_OnlyReturnsPendingAtOrBeforeNow()
    {
        var now = At(0, 12);

        _reminders.Create(new CreateReminderInput { Title = "过去", RemindAt = At(0, 11) });
        _reminders.Create(new CreateReminderInput { Title = "现在", RemindAt = now });
        _reminders.Create(new CreateReminderInput { Title = "未来", RemindAt = At(0, 13) });

        var due = _reminders.ListDue(now);

        Assert.Equal(["过去", "现在"], due.Select(r => r.Title).ToArray());
    }

    [Fact]
    public void ListDue_SkipsAlreadyFired()
    {
        var now = At(0, 12);
        var item = _reminders.Create(new CreateReminderInput { Title = "已弹", RemindAt = At(0, 11) });
        _reminders.SetStatus(item.Id, ReminderStatus.Fired);

        Assert.Empty(_reminders.ListDue(now));
        Assert.Single(_reminders.List(ReminderScope.Triggered));
    }

    [Fact]
    public void ListDue_RejectsNonWallClockNow()
    {
        Assert.Throws<ArgumentException>(() => _reminders.ListDue("2026-11-05T12:00:00Z"));
    }

    [Fact]
    public void Counts_TrackUnhandledAndUpcoming()
    {
        var fired = _reminders.Create(new CreateReminderInput { Title = "待处理", RemindAt = At(0, 11) });
        _reminders.SetStatus(fired.Id, ReminderStatus.Fired);
        _reminders.Create(new CreateReminderInput { Title = "待触发", RemindAt = At(1, 9) });

        var counts = _reminders.Counts();

        Assert.Equal(1, counts.Unhandled);
        Assert.Equal(1, counts.Upcoming);
    }

    [Fact]
    public void SetStatus_Done_MovesToHandledScope()
    {
        var item = _reminders.Create(new CreateReminderInput { Title = "喝水", RemindAt = At(0, 11) });
        _reminders.SetStatus(item.Id, ReminderStatus.Fired);
        _reminders.SetStatus(item.Id, ReminderStatus.Done);

        Assert.Empty(_reminders.List(ReminderScope.Triggered));
        var handled = Assert.Single(_reminders.List(ReminderScope.Handled));
        Assert.Equal(ReminderStatus.Done, handled.Status);
        Assert.NotNull(handled.FiredAt);
    }

    [Fact]
    public void SyncTaskDue_CreatesReminderFromDueAndLead()
    {
        var task = _tasks.Create(new CreateTaskInput
        {
            Title = "写周报",
            DueAt = At(1, 18),
            RemindBefore = 60
        });

        Assert.True(_reminders.SyncTaskDue(task));

        var item = Assert.Single(_reminders.List(ReminderScope.Upcoming));
        Assert.Equal(ReminderKind.TaskDue, item.Kind);
        Assert.Equal(task.Id, item.TaskId);
        Assert.Equal(At(1, 17), item.RemindAt);
        Assert.Equal("写周报", item.Title);
    }

    [Fact]
    public void SyncTaskDue_WithoutLead_DoesNothing()
    {
        var task = _tasks.Create(new CreateTaskInput { Title = "无提醒", DueAt = At(1, 18) });

        Assert.False(_reminders.SyncTaskDue(task));
        Assert.Empty(_reminders.List(ReminderScope.Upcoming));
    }

    [Fact]
    public void SyncTaskDue_IsIdempotent()
    {
        var task = _tasks.Create(new CreateTaskInput
        {
            Title = "重复同步",
            DueAt = At(1, 18),
            RemindBefore = 15
        });

        Assert.True(_reminders.SyncTaskDue(task));
        Assert.False(_reminders.SyncTaskDue(task));
        Assert.Single(_reminders.List(ReminderScope.Upcoming));
    }

    [Fact]
    public void SyncTaskDue_ChangedDueTime_ReplacesReminder()
    {
        var task = _tasks.Create(new CreateTaskInput
        {
            Title = "改期",
            DueAt = At(1, 18),
            RemindBefore = 60
        });
        _reminders.SyncTaskDue(task);

        var updated = _tasks.Update(task.Id, new UpdateTaskInput { DueAt = Optional<string?>.Some(At(2, 9)) })!;
        Assert.True(_reminders.SyncTaskDue(updated));

        var item = Assert.Single(_reminders.List(ReminderScope.Upcoming));
        Assert.Equal(At(2, 8), item.RemindAt);
    }

    [Fact]
    public void SyncTaskDue_DoneTask_DropsPendingReminder()
    {
        var task = _tasks.Create(new CreateTaskInput
        {
            Title = "完成就别提醒了",
            DueAt = At(1, 18),
            RemindBefore = 30
        });
        _reminders.SyncTaskDue(task);

        var done = _tasks.SetStatus(task.Id, TaskStatus.Done)!;
        Assert.True(_reminders.SyncTaskDue(done));

        Assert.Empty(_reminders.List(ReminderScope.Upcoming));
    }

    [Fact]
    public void List_TaskReminderFollowsTaskTitleAndProjectName()
    {
        var project = _projects.Create(new CreateProjectInput { Name = "年度总结" });
        var task = _tasks.Create(new CreateTaskInput
        {
            Title = "旧标题",
            ProjectId = project.Id,
            DueAt = At(1, 18),
            RemindBefore = 30
        });
        _reminders.SyncTaskDue(task);

        _tasks.Update(task.Id, new UpdateTaskInput { Title = Optional<string>.Some("新标题") });

        var item = Assert.Single(_reminders.List(ReminderScope.Upcoming));
        Assert.Equal("新标题", item.Title);
        Assert.Equal("年度总结", item.ProjectName);
    }

    [Fact]
    public void RemoveTask_CascadesReminders()
    {
        var task = _tasks.Create(new CreateTaskInput
        {
            Title = "会被删",
            DueAt = At(1, 18),
            RemindBefore = 30
        });
        _reminders.SyncTaskDue(task);

        _tasks.Remove(task.Id);

        Assert.Empty(_reminders.List(ReminderScope.Upcoming));
    }

    [Fact]
    public void Reschedule_KeepsPendingWithNewTime()
    {
        var item = _reminders.Create(new CreateReminderInput
        {
            Title = "每天喝水",
            RemindAt = At(0, 9),
            RepeatRule = RepeatRule.Daily
        });

        Assert.True(_reminders.Reschedule(item.Id, At(1, 9)));

        var reloaded = _reminders.Get(item.Id)!;
        Assert.Equal(ReminderStatus.Pending, reloaded.Status);
        Assert.Equal(At(1, 9), reloaded.RemindAt);
        Assert.Equal(RepeatRule.Daily, reloaded.RepeatRule);
    }

    [Fact]
    public void HandledScope_IsCappedAtTwenty()
    {
        for (var i = 0; i < 25; i++)
        {
            var item = _reminders.Create(new CreateReminderInput { Title = $"提醒 {i}", RemindAt = At(-1, 9) });
            _reminders.SetStatus(item.Id, ReminderStatus.Dismissed);
        }

        Assert.Equal(20, _reminders.List(ReminderScope.Handled).Count);
    }
}
