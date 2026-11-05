using Microsoft.Data.Sqlite;
using WorkStation.App.ViewModels;
using WorkStation.Core.Models;
using WorkStation.Core.Services;

namespace WorkStation.Tests;

/// <summary>命令面板：空态给什么、输入后怎么筛、选中后怎么执行。</summary>
public class CommandPaletteViewModelTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly WorkStationService _service;
    private readonly List<string> _executed = [];

    public CommandPaletteViewModelTests()
    {
        _connection = TestDb.OpenMigrated();
        _service = new WorkStationService(_connection, ":memory:");
    }

    public void Dispose() => _connection.Dispose();

    private CommandPaletteViewModel CreateVm()
    {
        var actions = new List<PaletteAction>
        {
            new("新建任务", "动作", "＋", ["add", "new", "task"], () => _executed.Add("新建任务")),
            new("设置", "跳转", "设", ["settings", "主题"], () => _executed.Add("设置"))
        };

        return new CommandPaletteViewModel(_service, actions);
    }

    [Fact]
    public void Open_WithEmptyQuery_ShowsActionsAndOpenTasks()
    {
        _service.TasksCreate(new CreateTaskInput { Title = "写周报" });
        _service.TasksCreate(new CreateTaskInput { Title = "已完成", Status = TaskStatus.Done });

        var vm = CreateVm();
        vm.Open();

        Assert.True(vm.IsOpen);
        Assert.Equal("新建任务", vm.Results[0].Title);
        Assert.Contains(vm.Results, r => r.Title == "写周报");
        Assert.DoesNotContain(vm.Results, r => r.Title == "已完成");
        Assert.Same(vm.Results[0], vm.SelectedItem);
    }

    [Fact]
    public void Query_FiltersActionsTasksAndProjects()
    {
        var project = _service.ProjectsCreate(new CreateProjectInput { Name = "工作台重构" }).Data!;
        _service.TasksCreate(new CreateTaskInput { Title = "重构数据库", ProjectId = project.Id });
        _service.TasksCreate(new CreateTaskInput { Title = "买牛奶" });

        var vm = CreateVm();
        vm.Open();

        vm.Query = "重构";

        Assert.Contains(vm.Results, r => r.Title == "重构数据库");
        Assert.Contains(vm.Results, r => r.Title == "工作台重构" && r.Hint == "项目");
        Assert.DoesNotContain(vm.Results, r => r.Title == "买牛奶");
        Assert.DoesNotContain(vm.Results, r => r.Title == "新建任务");
    }

    [Fact]
    public void Query_MatchesActionKeywords()
    {
        var vm = CreateVm();
        vm.Open();

        vm.Query = "new";

        Assert.Contains(vm.Results, r => r.Title == "新建任务");
    }

    [Fact]
    public void RunSelected_ClosesPaletteThenExecutes()
    {
        var vm = CreateVm();
        vm.Open();
        vm.SelectedItem = vm.Results.Single(r => r.Title == "设置");

        vm.RunSelectedCommand.Execute(null);

        Assert.False(vm.IsOpen);
        Assert.Equal(["设置"], _executed);
    }

    [Fact]
    public void ItemCommand_ActivatesThatRow()
    {
        var vm = CreateVm();
        vm.Open();

        var item = vm.Results.Single(r => r.Title == "新建任务");
        item.ActivateCommand.Execute(null);

        Assert.Equal(["新建任务"], _executed);
        Assert.False(vm.IsOpen);
    }

    [Fact]
    public void MoveSelection_WrapsAround()
    {
        var vm = CreateVm();
        vm.Open();
        var total = vm.Results.Count;
        Assert.True(total >= 2);

        vm.SelectedItem = vm.Results[0];
        vm.MoveSelection(-1);
        Assert.Same(vm.Results[total - 1], vm.SelectedItem);

        vm.MoveSelection(1);
        Assert.Same(vm.Results[0], vm.SelectedItem);
    }

    [Fact]
    public void TaskResult_InvokesOpenTaskCallback()
    {
        var task = _service.TasksCreate(new CreateTaskInput { Title = "写周报" }).Data!;
        var opened = new List<string>();

        var vm = CreateVm();
        vm.OpenTaskRequested = t => opened.Add(t.Id);
        vm.Open();
        vm.Query = "周报";

        vm.Results.Single(r => r.Title == "写周报").ActivateCommand.Execute(null);

        Assert.Equal([task.Id], opened);
    }

    [Fact]
    public void ProjectResult_InvokesOpenProjectCallback()
    {
        var project = _service.ProjectsCreate(new CreateProjectInput { Name = "工作台" }).Data!;
        var opened = new List<string>();

        var vm = CreateVm();
        vm.OpenProjectRequested = id => opened.Add(id);
        vm.Open();
        vm.Query = "工作台";

        vm.Results.Single(r => r.Hint == "项目").ActivateCommand.Execute(null);

        Assert.Equal([project.Id], opened);
    }

    [Fact]
    public void ShowEmpty_WhenNothingMatches()
    {
        var vm = CreateVm();
        vm.Open();

        vm.Query = "绝对搜不到的关键字";

        Assert.True(vm.ShowEmpty);
        Assert.Empty(vm.Results);
    }
}
