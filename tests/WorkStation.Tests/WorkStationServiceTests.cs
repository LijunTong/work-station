using Microsoft.Data.Sqlite;
using WorkStation.Core;
using WorkStation.Core.Models;
using WorkStation.Core.Services;

namespace WorkStation.Tests;

/// <summary>
/// 服务层契约：永远不向外抛异常，失败一律变成 Result。
/// 这是 UI 层唯一的数据入口，抛异常会直接崩窗口。
/// </summary>
public class WorkStationServiceTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly WorkStationService _service;

    public WorkStationServiceTests()
    {
        _connection = TestDb.OpenMigrated();
        _service = new WorkStationService(_connection, ":memory:", "0.1.0-test");
    }

    public void Dispose() => _connection.Dispose();

    [Fact]
    public void CreateProject_EmptyName_ReturnsValidationFailed()
    {
        var result = _service.ProjectsCreate(new CreateProjectInput { Name = "   " });

        Assert.False(result.Ok);
        Assert.Equal(ErrorCodes.ValidationFailed, result.Error?.Code);
        Assert.Contains("项目名称不能为空", result.Error?.Message);
    }

    [Fact]
    public void CreateTask_EmptyTitle_ReturnsValidationFailed()
    {
        var result = _service.TasksCreate(new CreateTaskInput { Title = "" });

        Assert.False(result.Ok);
        Assert.Equal(ErrorCodes.ValidationFailed, result.Error?.Code);
    }

    [Fact]
    public void UpdateProject_EmptyName_ReturnsValidationFailed()
    {
        var project = _service.ProjectsCreate(new CreateProjectInput { Name = "正常" }).Data!;

        var result = _service.ProjectsUpdate(project.Id, new UpdateProjectInput { Name = Optional<string>.Some("") });

        Assert.False(result.Ok);
        Assert.Equal(ErrorCodes.ValidationFailed, result.Error?.Code);
    }

    [Fact]
    public void DuplicateTagName_ReturnsDbWriteFailedWithChineseMessage()
    {
        _service.TagsCreate(new CreateTagInput { Name = "重复" });

        var result = _service.TagsCreate(new CreateTagInput { Name = "重复" });

        Assert.False(result.Ok);
        Assert.Equal(ErrorCodes.DbWriteFailed, result.Error?.Code);
        Assert.Contains("名称已存在", result.Error?.Message);
    }

    [Fact]
    public void MissingRecord_ReturnsSuccessWithNull()
    {
        var project = _service.ProjectsGet("missing");
        var update = _service.ProjectsUpdate("missing", new UpdateProjectInput { Name = Optional<string>.Some("x") });

        Assert.True(project.Ok);
        Assert.Null(project.Data);
        Assert.True(update.Ok);
        Assert.Null(update.Data);
    }

    [Fact]
    public void SettingsGet_ReturnsDefaultWhenMissing()
    {
        var result = _service.SettingsGet<string>("theme");

        Assert.True(result.Ok);
        Assert.Null(result.Data);
    }

    [Fact]
    public void SettingsRoundTrip()
    {
        Assert.True(_service.SettingsSet("theme", "dark").Ok);

        var result = _service.SettingsGet<string>("theme");

        Assert.True(result.Ok);
        Assert.Equal("dark", result.Data);
    }

    [Fact]
    public void Info_ExposesVersionAndPaths()
    {
        var info = _service.Info();

        Assert.Equal("0.1.0-test", info.Version);
        Assert.Equal(":memory:", info.DatabasePath);
    }

    [Fact]
    public void FullWorkflow_ProjectTaskProgress()
    {
        var project = _service.ProjectsCreate(new CreateProjectInput { Name = "端到端" }).Data!;
        _service.TasksCreate(new CreateTaskInput { Title = "一", ProjectId = project.Id, Status = TaskStatus.Done });
        _service.TasksCreate(new CreateTaskInput { Title = "二", ProjectId = project.Id });
        _service.TasksCreate(new CreateTaskInput { Title = "三", ProjectId = project.Id, Status = TaskStatus.Canceled });

        var tasks = _service.TasksList().Data!;

        // 取消的任务不参与进度分母：1 / 2 = 50%
        Assert.Equal(50, ProgressCalculator.Compute(tasks));
    }

    // ---------- 提醒 ----------

    private static string Local(DateTime value) => Core.Time.LocalTime.ToLocalDateTime(value);

    [Fact]
    public void CreateTask_WithLead_SchedulesReminder()
    {
        var due = DateTime.Today.AddDays(1).AddHours(18);

        var task = _service.TasksCreate(new CreateTaskInput
        {
            Title = "写周报",
            DueAt = Local(due),
            RemindBefore = 60
        }).Data!;

        var reminder = Assert.Single(_service.RemindersList(ReminderScope.Upcoming).Data!);
        Assert.Equal(task.Id, reminder.TaskId);
        Assert.Equal(Local(due.AddHours(-1)), reminder.RemindAt);
    }

    [Fact]
    public void UpdateTask_ClearingDue_RemovesPendingReminder()
    {
        var task = _service.TasksCreate(new CreateTaskInput
        {
            Title = "改期",
            DueAt = Local(DateTime.Today.AddDays(1).AddHours(18)),
            RemindBefore = 30
        }).Data!;

        _service.TasksUpdate(task.Id, new UpdateTaskInput { DueAt = Optional<string?>.Some(null) });

        Assert.Empty(_service.RemindersList(ReminderScope.Upcoming).Data!);
    }

    [Fact]
    public void UpdateTask_TurningReminderOff_RemovesPendingReminder()
    {
        var task = _service.TasksCreate(new CreateTaskInput
        {
            Title = "不提醒了我",
            DueAt = Local(DateTime.Today.AddDays(1).AddHours(18)),
            RemindBefore = 60
        }).Data!;
        Assert.Single(_service.RemindersList(ReminderScope.Upcoming).Data!);

        _service.TasksUpdate(task.Id, new UpdateTaskInput { RemindBefore = Optional<int?>.Some(0) });

        Assert.Empty(_service.RemindersList(ReminderScope.Upcoming).Data!);
    }

    /// <summary>
    /// 用户报过的问题：把任务的「提前提醒」改成不提醒，提醒页里那条还在。
    /// 原因是同步时只删 pending；已经弹过（fired）的那条没人管，会一直赖在待办里。
    /// </summary>
    [Fact]
    public void UpdateTask_TurningReminderOff_AlsoClearsAlreadyFiredOne()
    {
        var task = _service.TasksCreate(new CreateTaskInput
        {
            Title = "已经弹过了",
            DueAt = Local(DateTime.Today.AddDays(1).AddHours(18)),
            RemindBefore = 60
        }).Data!;

        var fired = Assert.Single(_service.RemindersList(ReminderScope.Upcoming).Data!);
        _service.RemindersSetStatus(fired.Id, ReminderStatus.Fired);
        Assert.Single(_service.RemindersList(ReminderScope.Triggered).Data!);

        _service.TasksUpdate(task.Id, new UpdateTaskInput { RemindBefore = Optional<int?>.Some(0) });

        Assert.Empty(_service.RemindersList(ReminderScope.Triggered).Data!);
        Assert.Empty(_service.RemindersList(ReminderScope.Upcoming).Data!);
    }

    [Fact]
    public void UpdateTask_DoesNotRearmAlreadyFiredReminder()
    {
        var task = _service.TasksCreate(new CreateTaskInput
        {
            Title = "改个标题而已",
            DueAt = Local(DateTime.Today.AddDays(1).AddHours(18)),
            RemindBefore = 60
        }).Data!;

        var fired = Assert.Single(_service.RemindersList(ReminderScope.Upcoming).Data!);
        _service.RemindersSetStatus(fired.Id, ReminderStatus.Fired);

        // 只改标题、提醒时间没变：不能把已触发的那条删掉重建，否则下一拍又弹一遍
        _service.TasksUpdate(task.Id, new UpdateTaskInput { Title = Optional<string>.Some("改过的标题") });

        Assert.Single(_service.RemindersList(ReminderScope.Triggered).Data!);
        Assert.Empty(_service.RemindersList(ReminderScope.Upcoming).Data!);
    }


    [Fact]
    public void TasksSetStatus_Done_RemovesPendingReminder()
    {
        var task = _service.TasksCreate(new CreateTaskInput
        {
            Title = "做完了",
            DueAt = Local(DateTime.Today.AddDays(1).AddHours(18)),
            RemindBefore = 30
        }).Data!;

        _service.TasksSetStatus(task.Id, TaskStatus.Done);

        Assert.Empty(_service.RemindersList(ReminderScope.Upcoming).Data!);
    }

    [Fact]
    public void RemindersSync_RepairsDriftFromDirectSqlEdit()
    {
        var task = _service.TasksCreate(new CreateTaskInput
        {
            Title = "被外部改了截止时间",
            DueAt = Local(DateTime.Today.AddDays(1).AddHours(18)),
            RemindBefore = 60
        }).Data!;
        Assert.Single(_service.RemindersList(ReminderScope.Upcoming).Data!);

        // 模拟「应用没开时用别的工具改了库」
        TestDb.Execute(_connection,
            $"UPDATE tasks SET due_at = '{Local(DateTime.Today.AddDays(2).AddHours(9))}' WHERE id = '{task.Id}'");

        var changed = _service.RemindersSync();

        Assert.True(changed.Data >= 1);
        var reminder = Assert.Single(_service.RemindersList(ReminderScope.Upcoming).Data!);
        Assert.Equal(Local(DateTime.Today.AddDays(2).AddHours(8)), reminder.RemindAt);
    }

    [Fact]
    public void DailyReviewSet_InvalidTime_ReturnsValidationFailed()
    {
        var result = _service.DailyReviewSet(new DailyReviewSettings { Enabled = true, Time = "25:00" });

        Assert.False(result.Ok);
        Assert.Equal(ErrorCodes.ValidationFailed, result.Error?.Code);
    }

    [Fact]
    public void DailyReviewSettings_DefaultToEnabledNineOClock()
    {
        var settings = _service.DailyReviewGet().Data!;

        Assert.True(settings.Enabled);
        Assert.Equal("09:00", settings.Time);
    }

    [Fact]
    public void CloseToTray_DefaultsTrue_AndPersists()
    {
        Assert.True(_service.CloseToTrayGet().Data);

        _service.CloseToTraySet(false);

        Assert.False(_service.CloseToTrayGet().Data);
    }
}
