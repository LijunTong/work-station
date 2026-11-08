using Avalonia.Headless.XUnit;
using Avalonia.Media;
using WorkStation.App.Controls;

namespace WorkStation.Tests;

/// <summary>
/// 图标几何表：路径写错了会在解析时抛异常 —— 那是界面启动时才炸的错。
/// 而 AppIcons 是静态类，静态构造失败 = TypeInitializationException，
/// 后面每个用到图标的页面都会跟着挂。这里先过一遍，把问题钉在测试里。
///
/// 必须是 AvaloniaFact：StreamGeometry 解析要拿到平台渲染接口，
/// 普通 [Fact] 没有初始化 Avalonia，连正确路径都会抛。
/// </summary>
public class AppIconsTests
{
    [AvaloniaFact]
    public void AllIconGeometries_ParseWithoutThrowing()
    {
        var icons = new StreamGeometry[]
        {
            AppIcons.Edit, AppIcons.Close, AppIcons.ArrowRight, AppIcons.Plus,
            AppIcons.Calendar, AppIcons.Check, AppIcons.Trash, AppIcons.Archive,
            AppIcons.Unarchive, AppIcons.Clock, AppIcons.Bell, AppIcons.Board,
            AppIcons.Tasks, AppIcons.Projects, AppIcons.Dashboard, AppIcons.Alert,
            AppIcons.Trend, AppIcons.SettingsLine, AppIcons.MoonLine, AppIcons.SunLine,
            AppIcons.Reports, AppIcons.ArrowLeft
        };

        foreach (var icon in icons)
        {
            Assert.NotNull(icon);
            Assert.True(icon!.Bounds.Width > 0 && icon.Bounds.Height > 0,
                $"图标几何为空：{icon.Bounds}");
        }
    }
}
