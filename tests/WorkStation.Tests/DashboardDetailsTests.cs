using System.Linq;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Microsoft.Data.Sqlite;
using WorkStation.App.Controls;
using WorkStation.App.ViewModels;
using WorkStation.App.Views;
using WorkStation.Core.Dashboard;
using WorkStation.Core.Models;
using WorkStation.Core.Services;
using WorkStation.Core.Time;
using Xunit;

namespace WorkStation.Tests;

/// <summary>
/// 仪表盘与看板的几处细节：
/// 清单行尾的日期（今天的不写）、趋势图的悬停提示、看板口径按钮的顺序。
/// </summary>
public class DashboardDetailsTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly WorkStationService _service;

    public DashboardDetailsTests()
    {
        _connection = TestDb.OpenMigrated();
        _service = new WorkStationService(_connection, ":memory:");
    }

    public void Dispose() => _connection.Dispose();

    /// <summary>
    /// 清单行尾要写日期，但「今天到期」的不写 ——
    /// 看「今日」口径时每行都写同一个日期，纯噪声；看「本月」时日期才是关键信息。
    /// </summary>
    [AvaloniaFact]
    public void TaskRow_ShowsDate_ButNotForToday()
    {
        var now = DateTime.Today.AddHours(10);
        var today = _service.TasksCreate(new CreateTaskInput
        {
            Title = "今天到期",
            DueAt = LocalTime.ToLocalDateTime(DateTime.Today.AddHours(18))
        }).Data!;
        var later = _service.TasksCreate(new CreateTaskInput
        {
            Title = "后天到期",
            DueAt = LocalTime.ToLocalDateTime(DateTime.Today.AddDays(2).AddHours(18))
        }).Data!;

        var vm = new DashboardViewModel(_service, (_, _) => { }, () => now);
        vm.Reload();

        var month = vm.Periods.First(p => p.Range == DashboardRange.Month);
        var todayItem = month.Tasks.First(t => t.Title == today.Title);
        var laterItem = month.Tasks.First(t => t.Title == later.Title);

        Assert.Equal(string.Empty, todayItem.DateLabel);
        Assert.False(todayItem.HasDateLabel);

        Assert.Equal(DateTime.Today.AddDays(2).ToString("MM-dd"), laterItem.DateLabel);
        Assert.True(laterItem.HasDateLabel);
    }

    /// <summary>趋势图每天都要能悬停出具体数字：柱子太细，光靠柱子本身点不准。</summary>
    [AvaloniaFact]
    public void TrendChart_EachDayHasHoverHint()
    {
        _service.TasksCreate(new CreateTaskInput { Title = "一件事" });

        var vm = new DashboardViewModel(_service, (_, _) => { });
        vm.Reload();
        var view = new DashboardView { DataContext = vm };
        var window = new Window { Width = 1280, Height = 820, Content = view };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();

        var chart = view.GetVisualDescendants().OfType<AppTrendChart>().Single();
        var tips = chart.GetVisualDescendants()
            .OfType<Border>()
            .Select(b => ToolTip.GetTip(b) as string)
            .Where(t => t is not null)
            .ToList();

        Assert.Equal(7, tips.Count);
        Assert.All(tips, t => Assert.Contains("完成", t));
        Assert.All(tips, t => Assert.Contains("到期", t));

        window.Close();
    }

    /// <summary>看板的口径按钮：逾期排在最后 —— 它是异常集合，不是时间段。</summary>
    [AvaloniaFact]
    public void BoardOverviewChips_PutOverdueLast()
    {
        var vm = new BoardViewModel(_service, (_, _) => { });

        Assert.Equal(["今日", "本周", "本月", "逾期"], vm.OverviewChips.Select(c => c.Label).ToArray());
    }
}
