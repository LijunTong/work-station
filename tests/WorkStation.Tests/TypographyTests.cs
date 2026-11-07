using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Microsoft.Data.Sqlite;
using SukiUI.Controls;
using WorkStation.App.ViewModels;
using WorkStation.App.Views;
using WorkStation.Core.Models;
using WorkStation.Core.Services;

namespace WorkStation.Tests;

/// <summary>
/// 版式一致性：字号必须落在统一阶梯上，侧边栏菜单文字不能被压扁，
/// 各页内容区必须共用同一条左基线。
/// </summary>
public class TypographyTests : IDisposable
{
    private readonly SqliteConnection _connection;

    public TypographyTests() => _connection = TestDb.OpenMigrated();

    public void Dispose() => _connection.Dispose();

    private MainWindowViewModel CreateVm(out Window window)
    {
        var vm = new MainWindowViewModel(new WorkStationService(_connection, ":memory:"));
        window = new MainWindow(vm) { Width = 1280, Height = 820 };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        return vm;
    }

    /// <summary>
    /// 侧边栏菜单文字的有效字号（要考虑 Viewbox 的缩放）。
    ///
    /// SukiUI 7.0.1 把 Header 塞进一个 Viewbox，槽位高度不够时按比例缩小 ——
    /// 曾经把 15px 的文字压到 0.4 倍（屏幕上只有 6px 高）。
    /// </summary>
    [AvaloniaFact]
    public void SideMenu_LabelsAreNotSquashedByViewbox()
    {
        var vm = CreateVm(out var window);

        foreach (var item in window.GetVisualDescendants().OfType<SukiSideMenuItem>())
        {
            var header = item.GetVisualDescendants().OfType<TextBlock>()
                .FirstOrDefault(t => t.Text == item.Header);
            Assert.NotNull(header);

            // 累乘路径上每一层的 RenderTransform 缩放
            var scale = 1.0;
            Visual? cur = header;
            while (cur is not null)
            {
                var m = cur.RenderTransform?.Value;
                if (m is { } mat && Math.Abs(mat.M11 - 1) > 0.001) scale *= mat.M11;
                cur = cur.GetVisualParent();
            }

            var effective = header.FontSize * scale;
            Assert.True(
                effective >= 11,
                $"菜单项「{item.Header}」被压缩到 {effective:F1}px（设定 {header.FontSize}px，缩放 {scale:F2}）");
        }

        window.Close();
    }

    /// <summary>
    /// 全站字号只能取自 App.axaml 定义的那几档。
    /// 之前散落着 18 种（含 10.5 / 11.5 / 12.5 / 13.5 / 14.5 等半像素），
    /// 半像素在小字号下会糊成锯齿，相邻两档也看不出层级。
    /// </summary>
    [AvaloniaFact]
    public void AllPages_UseOnlyTheSharedFontScale()
    {
        var vm = CreateVm(out var window);

        var allowed = new[]
        {
            11.0, 12.0, 13.0, 14.0, 16.0, 22.0, 24.0, 34.0
        };

        var offenders = new List<string>();

        foreach (var (label, act) in Pages())
        {
            act(vm);
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();

            foreach (var text in window.GetVisualDescendants().OfType<TextBlock>())
            {
                if (string.IsNullOrEmpty(text.Text)) continue;

                var size = Math.Round(text.FontSize, 1);
                if (!allowed.Contains(size))
                {
                    offenders.Add($"{label}: {size}px「{text.Text}」");
                }
            }
        }

        Assert.True(
            offenders.Count == 0,
            "以下字号不在统一阶梯内：\n  " + string.Join("\n  ", offenders.Distinct().Take(30)));

        window.Close();
    }

    /// <summary>
    /// 各页内容区共用同一条左基线。
    /// 之前页面标题在 x=256，工具栏却在 x=264/260 —— 上下错位很明显。
    ///
    /// 量的是**视觉块**（卡片 / 数据行 / 容器控件）的左边缘，不是文字：
    /// 文字的左边缘还受各自控件的内边距影响（按钮 12px、复选框 8px…），
    /// 拿文字比会得到一堆假差异，比基线该看的是块。
    /// </summary>
    [AvaloniaFact]
    public void AllPages_ShareOneLeftBaseline()
    {
        var vm = CreateVm(out var window);

        var baselines = new List<(string Label, double X)>();

        foreach (var (label, act) in Pages())
        {
            act(vm);
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();

            var page = window.GetVisualDescendants().OfType<UserControl>()
                .FirstOrDefault(u => u.GetType().Name != "MainWindow");
            if (page is null) continue;

            // 这一页最靠左的「块」：卡片、数据行，或工具栏里的容器控件。
            // 卡片有的是 Border、有的是 GlassCard，所以按「带 card 类的控件」找，不限类型。
            var blockLeft = page.GetVisualDescendants()
                .OfType<Control>()
                .Where(c => c.IsVisible)   // 隐藏的块不参与基线：它们根本没画出来
                .Where(c => c.Classes.Contains("card")
                            || c.Classes.Contains("card-row")
                            || c.Classes.Contains("column"))
                .Select(c => c.TranslatePoint(new Point(0, 0), window)?.X)
                .Where(x => x is not null)
                .Select(x => x!.Value)
                .ToList();

            // 没有卡片时（空页面）退回到工具栏控件
            if (blockLeft.Count == 0)
            {
                blockLeft = page.GetVisualDescendants()
                    .Where(c => c is ToggleButton or ComboBox or CheckBox)
                    .Select(c => c.TranslatePoint(new Point(0, 0), window)?.X)
                    .Where(x => x is not null)
                    .Select(x => x!.Value)
                    .ToList();
            }

            Assert.NotEmpty(blockLeft);
            baselines.Add((label, blockLeft.Min()));
        }

        var distinct = baselines.Select(b => Math.Round(b.X)).Distinct().ToArray();
        Assert.True(
            distinct.Length == 1,
            "各页左基线不一致：" + string.Join(" / ", baselines.Select(b => $"{b.Label}={b.X:F0}")));

        window.Close();
    }

    private static IEnumerable<(string Label, Action<MainWindowViewModel> Act)> Pages()
    {
        yield return ("任务页", vm => vm.ShowTasksCommand.Execute(null));
        yield return ("仪表盘", vm => vm.ShowDashboardCommand.Execute(null));
        yield return ("看板", vm => vm.ShowBoardCommand.Execute(null));
        yield return ("项目页", vm => vm.ShowProjectsCommand.Execute(null));
        yield return ("提醒页", vm => vm.ShowRemindersCommand.Execute(null));
    }
}
