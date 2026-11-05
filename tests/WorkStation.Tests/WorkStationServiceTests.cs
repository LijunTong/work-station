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
}
