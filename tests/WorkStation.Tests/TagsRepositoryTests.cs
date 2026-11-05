using Microsoft.Data.Sqlite;
using WorkStation.Core.Data.Repositories;
using WorkStation.Core.Models;

namespace WorkStation.Tests;

/// <summary>标签仓储：CRUD + 任务关联。</summary>
public class TagsRepositoryTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly TagsRepository _tags;
    private readonly TasksRepository _tasks;

    public TagsRepositoryTests()
    {
        _connection = TestDb.OpenMigrated();
        _tags = new TagsRepository(_connection);
        _tasks = new TasksRepository(_connection);
    }

    public void Dispose() => _connection.Dispose();

    [Fact]
    public void Create_ThenGetByName()
    {
        var created = _tags.Create(new CreateTagInput { Name = "紧急", Color = "#ff0000" });

        Assert.Equal("紧急", _tags.Get(created.Id)?.Name);
        Assert.Equal("#ff0000", _tags.GetByName("紧急")?.Color);
    }

    [Fact]
    public void Create_DuplicateName_Throws()
    {
        _tags.Create(new CreateTagInput { Name = "重复" });

        Assert.Throws<SqliteException>(() => _tags.Create(new CreateTagInput { Name = "重复" }));
    }

    [Fact]
    public void Update_PartialFieldsAndExplicitClear()
    {
        var tag = _tags.Create(new CreateTagInput { Name = "旧名", Color = "#00ff00" });

        Assert.Equal("新名", _tags.Update(tag.Id, new UpdateTagInput { Name = Optional<string>.Some("新名") })?.Name);
        Assert.Equal("#00ff00", _tags.Get(tag.Id)?.Color);
        Assert.Null(_tags.Update(tag.Id, new UpdateTagInput { Color = Optional<string?>.Some(null) })?.Color);
    }

    [Fact]
    public void SetTaskTags_ReplacesAndDeduplicates()
    {
        var task = _tasks.Create(new CreateTaskInput { Title = "打标签" });
        var a = _tags.Create(new CreateTagInput { Name = "A" });
        var b = _tags.Create(new CreateTagInput { Name = "B" });

        _tags.SetTaskTags(task.Id, [a.Id, b.Id, a.Id]);
        Assert.Equal(2, _tags.ListTaskTags(task.Id).Count);

        _tags.SetTaskTags(task.Id, [b.Id]);
        Assert.Equal([b.Id], _tags.ListTaskTags(task.Id).Select(t => t.Id));
    }

    [Fact]
    public void SetTaskTags_UnknownTag_RollsBackAndKeepsPrevious()
    {
        var task = _tasks.Create(new CreateTaskInput { Title = "回滚" });
        var tag = _tags.Create(new CreateTagInput { Name = "保留" });
        _tags.SetTaskTags(task.Id, [tag.Id]);

        Assert.Throws<SqliteException>(() => _tags.SetTaskTags(task.Id, [tag.Id, "missing"]));

        Assert.Equal([tag.Id], _tags.ListTaskTags(task.Id).Select(t => t.Id));
    }

    [Fact]
    public void Remove_DetachesTagFromTasks()
    {
        var task = _tasks.Create(new CreateTaskInput { Title = "还有任务" });
        var tag = _tags.Create(new CreateTagInput { Name = "会被删" });
        _tags.SetTaskTags(task.Id, [tag.Id]);

        Assert.True(_tags.Remove(tag.Id));
        Assert.Empty(_tags.ListTaskTags(task.Id));
        Assert.NotNull(_tasks.Get(task.Id));
    }

    [Fact]
    public void Remove_Missing_ReturnsFalse()
    {
        Assert.False(_tags.Remove("missing"));
    }

    [Fact]
    public void ListTaskTags_OrderedByName()
    {
        var task = _tasks.Create(new CreateTaskInput { Title = "排序" });
        var b = _tags.Create(new CreateTagInput { Name = "B" });
        var a = _tags.Create(new CreateTagInput { Name = "A" });

        _tags.SetTaskTags(task.Id, [b.Id, a.Id]);

        Assert.Equal(["A", "B"], _tags.ListTaskTags(task.Id).Select(t => t.Name));
    }

    [Fact]
    public void ListTagTaskIds_ReturnsTaskIds()
    {
        var task = _tasks.Create(new CreateTaskInput { Title = "反查" });
        var tag = _tags.Create(new CreateTagInput { Name = "反查标签" });
        _tags.SetTaskTags(task.Id, [tag.Id]);

        Assert.Equal([task.Id], _tags.ListTagTaskIds(tag.Id));
    }
}
