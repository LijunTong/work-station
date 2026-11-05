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
        Assert.Contains(vm.Results, r => r.Title == "新建任务");
        Assert.Contains(vm.Results, r => r.Title == "写周报");
        Assert.DoesNotContain(vm.Results, r => r.Title == "已完成");
        Assert.Same(vm.Results[0], vm.SelectedItem);
    }

    [Fact]
    public void Open_WithEmptyQuery_PutsContentBeforeCommands()
    {
        _service.ProjectsCreate(new CreateProjectInput { Name = "工作台重构" });
        _service.TasksCreate(new CreateTaskInput { Title = "写周报" });

        var vm = CreateVm();
        vm.Open();

        // 一屏只放得下九行左右，命令必须让位给项目与任务
        Assert.Equal("项目", vm.Results[0].Hint);
        Assert.Equal("写周报", vm.Results[1].Title);
        Assert.True(
            vm.Results.ToList().FindIndex(r => r.Title == "新建任务") > 1,
            "命令应排在项目与任务之后");
    }

    [Fact]
    public void Open_WithEmptyQuery_ShowsProjectsToo()
    {
        _service.ProjectsCreate(new CreateProjectInput { Name = "工作台重构" });

        var vm = CreateVm();
        vm.Open();

        // 空查询也要露项目：只在有输入时才列，面板看上去就像「不认识项目」
        Assert.Contains(vm.Results, r => r.Title == "工作台重构" && r.Hint == "项目");
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
