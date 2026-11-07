using Microsoft.Data.Sqlite;
using WorkStation.Core.Data.Repositories;
using WorkStation.Core.Models;

namespace WorkStation.Tests;

/// <summary>任务仓储。对应 Electron 版 tests/tasks.test.ts 的查询与状态流转部分。</summary>
public class TasksRepositoryTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly TasksRepository _tasks;
    private readonly ProjectsRepository _projects;

    public TasksRepositoryTests()
    {
        _connection = TestDb.OpenMigrated();
        _tasks = new TasksRepository(_connection);
        _projects = new ProjectsRepository(_connection);
    }

    public void Dispose() => _connection.Dispose();

    [Fact]
    public void Create_ThenGet_ReturnsDefaults()
    {
        var created = _tasks.Create(new CreateTaskInput { Title = "写周报" });

        Assert.Equal("写周报", created.Title);
        Assert.Equal(TaskStatus.Todo, created.Status);
        Assert.Equal(Priority.None, created.Priority);
        Assert.Null(created.ProjectId);
        Assert.Null(created.DueAt);
        Assert.Equal(created.Id, _tasks.Get(created.Id)?.Id);
    }

    [Fact]
    public void Create_TrimsNothingButRejectsInvalidDueAt()
    {
        var error = Assert.Throws<ArgumentException>(() =>
            _tasks.Create(new CreateTaskInput { Title = "坏的截止时间", DueAt = "2026-11-05T10:00:00.000Z" }));

        Assert.Contains("YYYY-MM-DDTHH:mm", error.Message);
    }

    /// <summary>任务也有起止两头：开始时间落库、读回、还能被清空。</summary>
    [Fact]
    public void Create_WithStartAt_StoresAndReadsBack()
    {
        var created = _tasks.Create(new CreateTaskInput
        {
            Title = "有起止",
            StartAt = "2026-12-01T00:00",
            DueAt = "2026-12-20T18:00"
        });

        Assert.Equal("2026-12-01T00:00", created.StartAt);
        Assert.Equal("2026-12-20T18:00", created.DueAt);
        Assert.Equal("2026-12-01T00:00", _tasks.Get(created.Id)?.StartAt);

        var cleared = _tasks.Update(created.Id, new UpdateTaskInput { StartAt = Optional<string?>.Some(null) });
        Assert.Null(cleared?.StartAt);
        // 没传的字段保持原值
        Assert.Equal("2026-12-20T18:00", cleared?.DueAt);
    }

    [Fact]
    public void Create_RejectsInvalidStartAt()
    {
        var error = Assert.Throws<ArgumentException>(() =>
            _tasks.Create(new CreateTaskInput { Title = "坏的开始时间", StartAt = "2026-12-01" }));

        Assert.Contains("YYYY-MM-DDTHH:mm", error.Message);
    }

    [Fact]
    public void Create_WithProject_StoresAssociation()
    {
        var project = _projects.Create(new CreateProjectInput { Name = "关联项目" });
        var task = _tasks.Create(new CreateTaskInput { Title = "子任务", ProjectId = project.Id });

        Assert.Equal(project.Id, _tasks.Get(task.Id)?.ProjectId);
    }

    [Fact]
    public void Update_OnlyOverwritesProvidedFields()
    {
        var task = _tasks.Create(new CreateTaskInput
        {
            Title = "原标题",
            Notes = "原备注",
            Priority = Priority.High,
            Estimate = 2.5
        });

        var updated = _tasks.Update(task.Id, new UpdateTaskInput { Title = Optional<string>.Some("新标题") });

        Assert.Equal("新标题", updated?.Title);
        Assert.Equal("原备注", updated?.Notes);
        Assert.Equal(Priority.High, updated?.Priority);
        Assert.Equal(2.5, updated?.Estimate);
    }

    [Fact]
    public void Update_ExplicitNull_ClearsNullableField()
    {
        var task = _tasks.Create(new CreateTaskInput
        {
            Title = "可清空",
            DueAt = "2026-11-06T18:00",
            BlockedReason = "等接口"
        });

        var updated = _tasks.Update(task.Id, new UpdateTaskInput { DueAt = Optional<string?>.Some(null) });

        Assert.Null(updated?.DueAt);
        Assert.Equal("等接口", updated?.BlockedReason);
    }

    [Fact]
    public void SetStatus_Done_WritesCompletedAt()
    {
        var task = _tasks.Create(new CreateTaskInput { Title = "做完它" });

        var done = _tasks.SetStatus(task.Id, TaskStatus.Done);

        Assert.Equal(TaskStatus.Done, done?.Status);
        Assert.NotNull(done?.CompletedAt);
    }

    [Fact]
    public void SetStatus_Doing_KeepsFirstStartedAt()
    {
        var task = _tasks.Create(new CreateTaskInput { Title = "开始做" });

        var first = _tasks.SetStatus(task.Id, TaskStatus.Doing);
        var second = _tasks.SetStatus(task.Id, TaskStatus.Doing);

        Assert.NotNull(first?.StartedAt);
        // 第二次进入 doing 不该覆盖首次开始时间
        Assert.Equal(first?.StartedAt, second?.StartedAt);
    }

    [Fact]
    public void SetStatus_MissingTask_ReturnsNull()
    {
        Assert.Null(_tasks.SetStatus("missing", TaskStatus.Done));
    }

    [Fact]
    public void List_FiltersByStatus()
    {
        var a = _tasks.Create(new CreateTaskInput { Title = "待办", Status = TaskStatus.Todo });
        _tasks.Create(new CreateTaskInput { Title = "进行中", Status = TaskStatus.Doing });

        var result = _tasks.List(new TaskFilter { Status = [TaskStatus.Todo] });

        Assert.Equal([a.Id], result.Select(t => t.Id));
    }

    [Fact]
    public void List_IncludeDoneFalse_ExcludesDoneAndCanceled()
    {
        var open = _tasks.Create(new CreateTaskInput { Title = "未完成" });
        _tasks.Create(new CreateTaskInput { Title = "完成", Status = TaskStatus.Done });
        _tasks.Create(new CreateTaskInput { Title = "取消", Status = TaskStatus.Canceled });

        var result = _tasks.List(new TaskFilter { IncludeDone = false });

        Assert.Equal([open.Id], result.Select(t => t.Id));
    }

    [Fact]
    public void List_ProjectIdSomeNull_ReturnsStandaloneOnly()
    {
        var project = _projects.Create(new CreateProjectInput { Name = "有项目" });
        var standalone = _tasks.Create(new CreateTaskInput { Title = "独立任务" });
        _tasks.Create(new CreateTaskInput { Title = "项目任务", ProjectId = project.Id });

        var result = _tasks.List(new TaskFilter { ProjectId = Optional<string?>.Some(null) });

        Assert.Equal([standalone.Id], result.Select(t => t.Id));
    }

    [Fact]
    public void List_Search_MatchesTitleAndNotes()
    {
        var byTitle = _tasks.Create(new CreateTaskInput { Title = "重构数据库" });
        var byNotes = _tasks.Create(new CreateTaskInput { Title = "别的事", Notes = "顺便重构一下" });
        _tasks.Create(new CreateTaskInput { Title = "无关" });

        var result = _tasks.List(new TaskFilter { Search = "重构" });

        Assert.Equal(2, result.Count);
        Assert.Contains(result, t => t.Id == byTitle.Id);
        Assert.Contains(result, t => t.Id == byNotes.Id);
    }

    [Fact]
    public void List_DueBeforeAndAfter_FilterByWallClock()
    {
        var early = _tasks.Create(new CreateTaskInput { Title = "早", DueAt = "2026-11-05T09:00" });
        var late = _tasks.Create(new CreateTaskInput { Title = "晚", DueAt = "2026-11-05T20:00" });

        var before = _tasks.List(new TaskFilter { DueBefore = "2026-11-05T12:00" });
        var after = _tasks.List(new TaskFilter { DueAfter = "2026-11-05T12:00" });

        Assert.Equal([early.Id], before.Select(t => t.Id));
        Assert.Equal([late.Id], after.Select(t => t.Id));
    }

    [Fact]
    public void CountByStatus_ReturnsZeroForUnusedStatuses()
    {
        _tasks.Create(new CreateTaskInput { Title = "一条", Status = TaskStatus.Blocked });

        var counts = _tasks.CountByStatus();

        Assert.Equal(5, counts.Count);
        Assert.Equal(1, counts[TaskStatus.Blocked]);
        Assert.Equal(0, counts[TaskStatus.Done]);
    }

    [Fact]
    public void CountOverdue_IgnoresFinishedAndFuture()
    {
        _tasks.Create(new CreateTaskInput { Title = "逾期", DueAt = "2000-01-01T10:00" });
        _tasks.Create(new CreateTaskInput { Title = "逾期但已完成", Status = TaskStatus.Done, DueAt = "2000-01-01T10:00" });
        _tasks.Create(new CreateTaskInput { Title = "还没到", DueAt = "2999-01-01T10:00" });

        Assert.Equal(1, _tasks.CountOverdue("2026-11-05T12:00"));
    }

    [Fact]
    public void CountOverdue_RejectsDateOnlyFormat()
    {
        Assert.Throws<ArgumentException>(() => _tasks.CountOverdue("2026-11-05"));
    }

    [Fact]
    public void Remove_ReturnsTrueThenFalse()
    {
        var task = _tasks.Create(new CreateTaskInput { Title = "待删" });

        Assert.True(_tasks.Remove(task.Id));
        Assert.False(_tasks.Remove(task.Id));
    }
}
