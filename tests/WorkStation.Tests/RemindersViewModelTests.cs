using Microsoft.Data.Sqlite;
using WorkStation.App.ViewModels;
using WorkStation.Core.Models;
using WorkStation.Core.Services;
using WorkStation.Core.Time;

namespace WorkStation.Tests;

/// <summary>提醒页：三组数据、新建抽屉、完成/忽略/稍后/删除。</summary>
public class RemindersViewModelTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly WorkStationService _service;
    private readonly DateTime _now = new(2026, 11, 5, 12, 0, 0, DateTimeKind.Local);

    public RemindersViewModelTests()
    {
        _connection = TestDb.OpenMigrated();
        _service = new WorkStationService(_connection, ":memory:");
    }

    public void Dispose() => _connection.Dispose();

    private RemindersViewModel NewViewModel() =>
        new(_service, (_, _) => { }, () => _now);

    private static string Local(DateTime value) => LocalTime.ToLocalDateTime(value);

    private ReminderItem Create(string title, DateTime at, RepeatRule repeat = RepeatRule.None) =>
        _service.RemindersCreate(new CreateReminderInput
        {
            Title = title,
            RemindAt = Local(at),
            RepeatRule = repeat
        }).Data!;

    [Fact]
    public void Reload_SplitsIntoThreeGroups()
    {
        var a = Create("待处理", _now.AddHours(-1));
        _service.RemindersSetStatus(a.Id, ReminderStatus.Fired);
        Create("待触发", _now.AddHours(2));
        var done = Create("已处理", _now.AddHours(-3));
        _service.RemindersSetStatus(done.Id, ReminderStatus.Done);

        var vm = NewViewModel();
        vm.Reload();

        Assert.Single(vm.Triggered);
        Assert.Single(vm.Upcoming);
        Assert.Single(vm.Handled);
        Assert.False(vm.ShowEmpty);
        Assert.True(vm.HasTriggered);
    }

    [Fact]
    public void Reload_EmptyStateOnlyWhenNothingAtAll()
    {
        var vm = NewViewModel();
        vm.Reload();

        Assert.True(vm.ShowEmpty);
        Assert.False(vm.HasTriggered);
    }

    [Fact]
    public void NewReminder_DefaultTimeIsHalfAnHourLater()
    {
        var vm = NewViewModel();
        vm.NewReminderCommand.Execute(null);

        Assert.True(vm.IsFormOpen);
        Assert.Equal(_now.Date, vm.Form!.Date);
        Assert.Equal(new TimeSpan(12, 30, 0), vm.Form.Time);
    }

    [Fact]
    public void NewReminder_SavesAndAppearsInUpcoming()
    {
        var vm = NewViewModel();
        vm.NewReminderCommand.Execute(null);
        vm.Form!.Title = "交电费";
        vm.Form.Date = _now.Date.AddDays(1);
        vm.Form.Time = new TimeSpan(9, 0, 0);
        vm.Form.RepeatIndex = 1;
        vm.Form.SaveCommand.Execute(null);

        Assert.False(vm.IsFormOpen);
        var row = Assert.Single(vm.Upcoming);
        Assert.Equal("交电费", row.Title);
        Assert.Equal("每天", row.RepeatText);
        Assert.Equal("明天 09:00", row.TimeText);
    }

    [Fact]
    public void NewReminder_RejectsEmptyTitle()
    {
        var vm = NewViewModel();
        vm.NewReminderCommand.Execute(null);
        vm.Form!.SaveCommand.Execute(null);

        Assert.True(vm.IsFormOpen);
        Assert.Contains("不能为空", vm.Form.Error);
        Assert.Empty(vm.Upcoming);
    }

    [Fact]
    public void Complete_MovesTriggeredToHandled()
    {
        var item = Create("该处理了", _now.AddHours(-1));
        _service.RemindersSetStatus(item.Id, ReminderStatus.Fired);

        var vm = NewViewModel();
        vm.Reload();
        vm.Triggered[0].CompleteCommand.Execute(null);

        Assert.Empty(vm.Triggered);
        Assert.Single(vm.Handled);
        Assert.Equal("已处理", vm.Handled[0].StatusText);
    }

    [Fact]
    public void Dismiss_AlsoClearsTriggered()
    {
        var item = Create("忽略它", _now.AddHours(-1));
        _service.RemindersSetStatus(item.Id, ReminderStatus.Fired);

        var vm = NewViewModel();
        vm.Reload();
        vm.Triggered[0].DismissCommand.Execute(null);

        Assert.Empty(vm.Triggered);
        Assert.Equal("已忽略", vm.Handled[0].StatusText);
    }

    [Fact]
    public void Snooze_PushesTenMinutesAndKeepsPending()
    {
        var item = Create("等会儿", _now.AddHours(-1));
        _service.RemindersSetStatus(item.Id, ReminderStatus.Fired);

        var vm = NewViewModel();
        vm.Reload();
        vm.Triggered[0].SnoozeCommand.Execute(null);

        Assert.Empty(vm.Triggered);
        var row = Assert.Single(vm.Upcoming);
        Assert.Equal(Local(_now.AddMinutes(10)), _service.RemindersGet(item.Id).Data!.RemindAt);
        Assert.Equal("今天 12:10", row.TimeText);
    }

    [Fact]
    public void Delete_AsksForConfirmationFirst()
    {
        Create("删掉我", _now.AddHours(2));

        var vm = NewViewModel();
        vm.Reload();
        vm.Upcoming[0].DeleteCommand.Execute(null);

        Assert.True(vm.IsDeleteConfirmOpen);
        Assert.Single(vm.Upcoming);

        vm.ConfirmDeleteCommand.Execute(null);

        Assert.False(vm.IsDeleteConfirmOpen);
        Assert.Empty(vm.Upcoming);
    }

    [Fact]
    public void TaskReminder_ShowsProjectAndSource()
    {
        var project = _service.ProjectsCreate(new CreateProjectInput { Name = "年度总结" }).Data!;
        _service.TasksCreate(new CreateTaskInput
        {
            Title = "写总结",
            ProjectId = project.Id,
            DueAt = Local(_now.AddHours(6)),
            RemindBefore = 60
        });

        var vm = NewViewModel();
        vm.Reload();

        var row = Assert.Single(vm.Upcoming);
        Assert.Equal("写总结", row.Title);
        Assert.Equal("任务截止 · 年度总结", row.MetaText);
        Assert.False(row.IsTriggered);
    }

    [Theory]
    [InlineData("2026-11-05T18:00", "今天 18:00")]
    [InlineData("2026-11-06T09:00", "明天 09:00")]
    [InlineData("2026-11-04T21:30", "昨天 21:30")]
    [InlineData("2026-11-09T09:00", "周一 09:00")]
    [InlineData("2026-12-20T09:00", "12-20 09:00")]
    [InlineData("2027-01-05T09:00", "2027-01-05 09:00")]
    public void FormatTime_UsesRelativeDayWhenClose(string stored, string expected)
    {
        Assert.Equal(expected, ReminderRowViewModel.FormatTime(stored, _now));
    }
}
