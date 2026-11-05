using Microsoft.Data.Sqlite;
using WorkStation.App.ViewModels;
using WorkStation.Core.Models;
using WorkStation.Core.Services;

namespace WorkStation.Tests;

/// <summary>项目页视图模型：创建/编辑/归档/删除与进度口径。</summary>
public class ProjectsViewModelTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly WorkStationService _service;
    private readonly List<(string Message, bool IsError)> _toasts = [];

    public ProjectsViewModelTests()
    {
        _connection = TestDb.OpenMigrated();
        _service = new WorkStationService(_connection, ":memory:");
    }

    public void Dispose() => _connection.Dispose();

    private ProjectsViewModel CreateVm()
    {
        var vm = new ProjectsViewModel(_service, (message, isError) => _toasts.Add((message, isError)));
        vm.Reload();
        return vm;
    }

    [Fact]
    public void Reload_Empty_ShowsEmptyState()
    {
        var vm = CreateVm();

        Assert.True(vm.ShowEmpty);
        Assert.False(vm.HasError);
    }

    [Fact]
    public void NewProject_Save_CreatesCard()
    {
        var vm = CreateVm();
        vm.NewProjectCommand.Execute(null);
        vm.Form!.Name = "重构工作台";
        vm.Form.Description = "迁移到 Avalonia";

        vm.Form.SaveCommand.Execute(null);

        Assert.False(vm.IsFormOpen);
        Assert.Single(vm.Projects);
        Assert.Equal("重构工作台", vm.Projects[0].Name);
        Assert.False(vm.ShowEmpty);
    }

    [Fact]
    public void NewProject_EmptyName_ShowsValidationError()
    {
        var vm = CreateVm();
        vm.NewProjectCommand.Execute(null);

        vm.Form!.SaveCommand.Execute(null);

        Assert.True(vm.IsFormOpen);
        Assert.Equal("项目名称不能为空", vm.Form!.Error);
        Assert.Empty(vm.Projects);
    }

    [Fact]
    public void NewProject_DueBeforeStart_ShowsValidationError()
    {
        var vm = CreateVm();
        vm.NewProjectCommand.Execute(null);
        vm.Form!.Name = "顺序错";
        vm.Form.StartDate = new DateTime(2026, 12, 10);
        vm.Form.DueDate = new DateTime(2026, 12, 1);

        vm.Form.SaveCommand.Execute(null);

        Assert.Contains("不能早于", vm.Form.Error);
    }

    [Fact]
    public void EditProject_PrefillsForm()
    {
        var project = _service.ProjectsCreate(new CreateProjectInput
        {
            Name = "原项目",
            Description = "原描述",
            Priority = Priority.High
        }).Data!;
        var vm = CreateVm();

        vm.EditProjectCommand.Execute(vm.Projects[0]);

        Assert.True(vm.Form!.IsEditing);
        Assert.Equal(project.Id, vm.Form.ProjectId);
        Assert.Equal("原项目", vm.Form.Name);
        Assert.Equal(3, vm.Form.PriorityIndex);
    }

    [Fact]
    public void Archive_HidesByDefaultAndToggleShowsIt()
    {
        var vm = CreateVm();
        vm.NewProjectCommand.Execute(null);
        vm.Form!.Name = "要归档的";
        vm.Form.SaveCommand.Execute(null);

        vm.Projects[0].ToggleArchiveCommand.Execute(null);

        Assert.Empty(vm.Projects);

        vm.IncludeArchived = true;

        Assert.Single(vm.Projects);
        Assert.True(vm.Projects[0].IsArchived);
        Assert.Equal("取消归档", vm.Projects[0].ArchiveLabel);
    }

    [Fact]
    public void Delete_RequiresConfirmation()
    {
        var project = _service.ProjectsCreate(new CreateProjectInput { Name = "要删的" }).Data!;
        var vm = CreateVm();

        vm.Projects[0].DeleteCommand.Execute(null);
        Assert.True(vm.IsDeleteConfirmOpen);
        Assert.NotNull(_service.ProjectsGet(project.Id).Data);

        vm.ConfirmDeleteCommand.Execute(null);

        Assert.False(vm.IsDeleteConfirmOpen);
        Assert.Null(_service.ProjectsGet(project.Id).Data);
    }

    [Fact]
    public void Progress_AutoCountsDoneExcludingCanceled()
    {
        var project = _service.ProjectsCreate(new CreateProjectInput { Name = "自动进度" }).Data!;
        _service.TasksCreate(new CreateTaskInput { Title = "一", ProjectId = project.Id, Status = TaskStatus.Done });
        _service.TasksCreate(new CreateTaskInput { Title = "二", ProjectId = project.Id, Status = TaskStatus.Todo });
        _service.TasksCreate(new CreateTaskInput { Title = "三", ProjectId = project.Id, Status = TaskStatus.Canceled });

        var vm = CreateVm();

        Assert.Equal(50, vm.Projects[0].Progress);
        Assert.Equal("50%", vm.Projects[0].ProgressText);
    }

    [Fact]
    public void Progress_ManualOverridesTasks()
    {
        var project = _service.ProjectsCreate(new CreateProjectInput
        {
            Name = "手动进度",
            ProgressMode = ProgressMode.Manual,
            ProgressManual = 80
        }).Data!;
        _service.TasksCreate(new CreateTaskInput { Title = "一", ProjectId = project.Id, Status = TaskStatus.Done });

        var vm = CreateVm();

        Assert.Equal(80, vm.Projects[0].Progress);
    }

    [Fact]
    public void Meta_ShowsTaskCounts()
    {
        var project = _service.ProjectsCreate(new CreateProjectInput { Name = "统计" }).Data!;
        _service.TasksCreate(new CreateTaskInput { Title = "一", ProjectId = project.Id, Status = TaskStatus.Doing });
        _service.TasksCreate(new CreateTaskInput { Title = "二", ProjectId = project.Id });

        var vm = CreateVm();

        Assert.Equal("2 个任务 · 进行中 1", vm.Projects[0].MetaText);
    }
}
