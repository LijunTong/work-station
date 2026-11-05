using Microsoft.Data.Sqlite;
using WorkStation.Core.Data.Repositories;
using WorkStation.Core.Models;
using WorkStation.Core.Time;

namespace WorkStation.Tests;

/// <summary>
/// 项目仓储测试。逐条对应 Electron 版 tests/projects.test.ts。
/// </summary>
public class ProjectsRepositoryTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly ProjectsRepository _repo;
    private readonly TasksRepository _tasks;

    public ProjectsRepositoryTests()
    {
        _connection = TestDb.OpenMigrated();
        _repo = new ProjectsRepository(_connection);
        _tasks = new TasksRepository(_connection);
    }

    public void Dispose() => _connection.Dispose();

    [Fact]
    public void Create_ThenGetById_ReturnsDefaults()
    {
        var created = _repo.Create(new CreateProjectInput { Name = "重构工作台" });

        Assert.Equal("重构工作台", created.Name);
        Assert.Equal(ProjectStatus.Planning, created.Status);
        Assert.Equal(ProgressMode.Auto, created.ProgressMode);
        Assert.Null(created.ArchivedAt);
        Assert.Equal(created.Id, _repo.Get(created.Id)?.Id);
    }

    [Fact]
    public void List_ByDefault_ExcludesArchived()
    {
        var a = _repo.Create(new CreateProjectInput { Name = "在做的" });
        var b = _repo.Create(new CreateProjectInput { Name = "做完的" });
        _repo.Archive(b.Id);

        Assert.Equal([a.Id], _repo.List().Select(p => p.Id));
    }

    [Fact]
    public void List_WithIncludeArchived_ReturnsAll()
    {
        _repo.Create(new CreateProjectInput { Name = "在做的" });
        var b = _repo.Create(new CreateProjectInput { Name = "做完的" });
        _repo.Archive(b.Id);

        Assert.Equal(2, _repo.List(includeArchived: true).Count);
    }

    [Fact]
    public void Unarchive_BringsProjectBackToDefaultList()
    {
        var b = _repo.Create(new CreateProjectInput { Name = "做完的" });
        _repo.Archive(b.Id);
        _repo.Unarchive(b.Id);

        Assert.Single(_repo.List());
    }

    [Fact]
    public void Update_OnlyOverwritesProvidedFields()
    {
        var created = _repo.Create(new CreateProjectInput
        {
            Name = "原名",
            Description = "原描述",
            Priority = Priority.High
        });

        var updated = _repo.Update(created.Id, new UpdateProjectInput { Name = Optional<string>.Some("新名") });

        Assert.Equal("新名", updated?.Name);
        Assert.Equal("原描述", updated?.Description);
        Assert.Equal(Priority.High, updated?.Priority);
    }

    [Fact]
    public void Update_NullableField_DistinguishesAbsentFromNull()
    {
        var created = _repo.Create(new CreateProjectInput
        {
            Name = "可空字段",
            Color = "#ff0000",
            DueDate = "2026-12-01"
        });

        // 没传 color：保持原值
        Assert.Equal("#ff0000", _repo.Update(created.Id, new UpdateProjectInput { Name = Optional<string>.Some("改名") })?.Color);
        // 传了 color: null：显式清空
        Assert.Null(_repo.Update(created.Id, new UpdateProjectInput { Color = Optional<string?>.Some(null) })?.Color);
        // 没传 dueDate：保持原值（不能被「没传」覆盖成 null）
        Assert.Equal("2026-12-01", _repo.Update(created.Id, new UpdateProjectInput { Name = Optional<string>.Some("再改名") })?.DueDate);
    }

    [Fact]
    public void Update_MissingProject_ReturnsNull()
    {
        Assert.Null(_repo.Update("missing", new UpdateProjectInput { Name = Optional<string>.Some("x") }));
    }

    [Fact]
    public void Remove_ReturnsTrueThenFalse()
    {
        var created = _repo.Create(new CreateProjectInput { Name = "待删" });

        Assert.True(_repo.Remove(created.Id));
        Assert.False(_repo.Remove(created.Id));
    }

    [Fact]
    public void GetByName_HitsExistingName()
    {
        _repo.Create(new CreateProjectInput { Name = "唯一名字" });

        Assert.Equal("唯一名字", _repo.GetByName("唯一名字")?.Name);
        Assert.Null(_repo.GetByName("不存在"));
    }

    [Fact]
    public void Create_ManualProgress_StoresPercentage()
    {
        var created = _repo.Create(new CreateProjectInput
        {
            Name = "手动进度",
            ProgressMode = ProgressMode.Manual,
            ProgressManual = 42
        });

        Assert.Equal(42, _repo.Get(created.Id)?.ProgressManual);
    }

    [Fact]
    public void RepositoryMethods_CanBeCalledThroughLocalDelegates()
    {
        // Electron 版要单独测「this 绑定」是因为 JS 的方法可以脱离对象被解构出来。
        // C# 的实例方法本来就绑定实例，这里用等价写法守住同一件事：方法能脱离调用点单独引用。
        Func<CreateProjectInput, Project> create = _repo.Create;
        Func<string, Project?> get = _repo.Get;
        Func<string, UpdateProjectInput, Project?> update = _repo.Update;

        var created = create(new CreateProjectInput { Name = "解构调用" });

        Assert.Equal("解构调用", get(created.Id)?.Name);
        Assert.Equal("解构改名", update(created.Id, new UpdateProjectInput { Name = Optional<string>.Some("解构改名") })?.Name);
    }

    [Fact]
    public void Archive_WritesSameTimestampToArchivedAtAndUpdatedAt()
    {
        var created = _repo.Create(new CreateProjectInput { Name = "归档时间戳" });
        _repo.Archive(created.Id);

        var archived = _repo.Get(created.Id);

        Assert.NotNull(archived?.ArchivedAt);
        Assert.Equal(archived?.ArchivedAt, archived?.UpdatedAt);
    }

    [Fact]
    public void CountTasks_CountsTotalDoneAndDoing()
    {
        var project = _repo.Create(new CreateProjectInput { Name = "统计项目" });
        _tasks.Create(new CreateTaskInput { Title = "一", ProjectId = project.Id, Status = TaskStatus.Done });
        _tasks.Create(new CreateTaskInput { Title = "二", ProjectId = project.Id, Status = TaskStatus.Doing });
        _tasks.Create(new CreateTaskInput { Title = "三", ProjectId = project.Id, Status = TaskStatus.Todo });

        var counts = _repo.CountTasks(project.Id);

        Assert.Equal(3, counts.Total);
        Assert.Equal(1, counts.Done);
        Assert.Equal(1, counts.Doing);
    }

    [Fact]
    public void CountTasks_EmptyProject_ReturnsAllZero()
    {
        var project = _repo.Create(new CreateProjectInput { Name = "空项目" });

        var counts = _repo.CountTasks(project.Id);

        Assert.Equal(0, counts.Total);
        Assert.Equal(0, counts.Done);
        Assert.Equal(0, counts.Doing);
        Assert.Equal(0, counts.Overdue);
    }

    [Fact]
    public void CountTasks_CountsOverdueUnfinishedTasks()
    {
        var project = _repo.Create(new CreateProjectInput { Name = "逾期项目" });
        _tasks.Create(new CreateTaskInput
        {
            Title = "旧的",
            ProjectId = project.Id,
            Status = TaskStatus.Todo,
            DueAt = "2000-01-01T10:00"
        });

        Assert.Equal(1, _repo.CountTasks(project.Id).Overdue);
    }

    [Fact]
    public void CountTasks_DoesNotCountOverdueFinishedTasks()
    {
        var project = _repo.Create(new CreateProjectInput { Name = "过期但已结束" });
        _tasks.Create(new CreateTaskInput
        {
            Title = "做完了",
            ProjectId = project.Id,
            Status = TaskStatus.Done,
            DueAt = "2000-01-01T10:00"
        });
        _tasks.Create(new CreateTaskInput
        {
            Title = "取消了",
            ProjectId = project.Id,
            Status = TaskStatus.Canceled,
            DueAt = "2000-01-01T10:00"
        });

        var counts = _repo.CountTasks(project.Id);

        Assert.Equal(0, counts.Overdue);
        Assert.Equal(2, counts.Total);
        Assert.Equal(1, counts.Done);
    }

    [Fact]
    public void CountTasks_UsesWallClock_PastTimeIsOverdue()
    {
        // 墙钟口径：同一天里时刻已过就算逾期（Electron 版用冻结时钟固定在 10:00）
        var project = _repo.Create(new CreateProjectInput { Name = "墙钟口径" });
        var past = LocalTime.ToLocalDateTime(DateTime.Now.AddHours(-1));
        _tasks.Create(new CreateTaskInput
        {
            Title = "已经过的",
            ProjectId = project.Id,
            Status = TaskStatus.Todo,
            DueAt = past
        });

        Assert.Equal(1, _repo.CountTasks(project.Id).Overdue);
    }

    [Fact]
    public void CountTasks_UsesWallClock_FutureTimeIsNotOverdue()
    {
        var project = _repo.Create(new CreateProjectInput { Name = "今天稍后" });
        var future = LocalTime.ToLocalDateTime(DateTime.Now.AddHours(1));
        _tasks.Create(new CreateTaskInput
        {
            Title = "还没到的",
            ProjectId = project.Id,
            Status = TaskStatus.Todo,
            DueAt = future
        });

        Assert.Equal(0, _repo.CountTasks(project.Id).Overdue);
    }

    [Fact]
    public void Remove_CascadesToProjectTasks()
    {
        // tasks.project_id 上的外键是 ON DELETE CASCADE：删项目连带删任务
        var project = _repo.Create(new CreateProjectInput { Name = "级联删除" });
        var task = _tasks.Create(new CreateTaskInput { Title = "跟着删", ProjectId = project.Id });

        Assert.True(_repo.Remove(project.Id));

        Assert.Null(_tasks.Get(task.Id));
    }
}
