using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using SukiUI.Utilities.Effects;
using WorkStation.App.Views;
using Xunit;

namespace WorkStation.Tests;

/// <summary>
/// 主窗口的背景 shader 守卫。
///
/// 为什么需要单独测：整窗背景（含标题栏）走的是 SukiUI 的
/// <c>SukiBackground</c>，它是个 composition 自定义视觉 —— 无头渲染里完全不画，
/// VisualCheck 截图看不到它，出问题只能等用户打开真实窗口才发现。
/// 而 <see cref="SukiEffect.FromString"/> 是预编译的：语法错、少了 return、
/// 类型不匹配都会直接抛异常，所以在这里编译一次就能挡住绝大多数致命错误。
///
/// 背景色本身没法在这里断言（无头下没有像素），要验观感只能开真实窗口。
/// </summary>
public sealed class BackdropShaderTests
{
    /// <summary>SukiUI 在 EffectDrawBase 里注入的全部 uniform。</summary>
    private static readonly HashSet<string> InjectedUniforms = new(StringComparer.Ordinal)
    {
        "iTime", "iDark", "iAlpha", "iResolution", "iPrimary", "iAccent", "iBase"
    };

    [AvaloniaFact]
    public void BackdropShader_Compiles()
    {
        var effect = SukiEffect.FromString(MainWindow.BackdropShader);

        Assert.NotNull(effect);
    }

    /// <summary>
    /// shader 里用到的 iXxx 必须都是 SukiUI 会送值的那些。
    /// 自己编一个 uniform 不会编译报错，运行时拿到的却是 0 —— 整块背景变黑，
    /// 而且无头截图看不出来，属于最难查的一类问题。
    /// </summary>
    [Fact]
    public void BackdropShader_UsesOnlyInjectedUniforms()
    {
        var used = Regex.Matches(MainWindow.BackdropShader, @"\bi[A-Z]\w*")
            .Select(m => m.Value)
            .Distinct()
            .ToList();

        Assert.NotEmpty(used);

        foreach (var name in used)
        {
            Assert.True(
                InjectedUniforms.Contains(name),
                $"shader 用了未注入的 uniform '{name}'；可用的是：{string.Join(" / ", InjectedUniforms)}");
        }
    }

    /// <summary>
    /// 边缘淡出依赖一个几何假设：SukiBackground 带 Margin=-150，
    /// 它的绘制层尺寸按 Bounds 算，所以可视区左上角在 shader 坐标里是 (150,150)。
    /// 这个假设一旦不成立，淡出会画到窗口内部大约 100px 的位置 —— 变成一条
    /// 突兀的白色渐隐。这里把布局那一半钉死，映射的推导才有依据。
    /// </summary>
    [AvaloniaFact]
    public void SukiBackgroundLayer_StartsOneHundredFiftyPixelsOutsideThePanel()
    {
        var probe = new Border { Margin = new Thickness(-150) };
        var host = new Panel { Width = 400, Height = 300 };
        host.Children.Add(probe);

        var window = new Window { Width = 400, Height = 300, Content = host };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();

        // 负边距把元素撑到父级之外：原点在 (-150,-150)，尺寸是父级 + 300
        Assert.Equal(-150, probe.Bounds.X);
        Assert.Equal(-150, probe.Bounds.Y);
        Assert.Equal(700, probe.Bounds.Width);
        Assert.Equal(600, probe.Bounds.Height);

        window.Close();
    }
}
