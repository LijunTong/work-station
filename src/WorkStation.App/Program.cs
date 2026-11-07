using Avalonia;
using Avalonia.Media;
using WorkStation.App.Platform;

namespace WorkStation.App;

internal static class Program
{
    /// <summary>
    /// 单实例名。改这里等于允许新旧两版同时跑（升级期间用户往往两个都开着）。
    /// </summary>
    private const string InstanceName = "WorkStation";

    // Avalonia 没有 App.xaml 的 StartupUri 惯例，入口必须显式构建 AppBuilder。
    // UsePlatformDetect 会按平台挑渲染后端：Windows=Direct2D/X11，macOS=Metal，Linux=X11/Wayland。
    [STAThread]
    public static void Main(string[] args)
    {
        // 第二个实例自己退出，只把「有人又点了一次」通过管道发给第一个
        if (!SingleInstance.TryStart(InstanceName))
        {
            SingleInstance.SignalExisting(InstanceName);
            return;
        }

        try
        {
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }
        finally
        {
            SingleInstance.Stop();
        }
    }

    // 由 Avalonia.Headless 的测试宿主复用，因此必须是 public 且无参可调。
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            // 中文字体必须显式指定。Inter 不含 CJK 字形，之前所有中文都在走系统
            // 回退字体：同一行里西文是 Inter 的几何字形、中文是另一种字体，
            // 笔画重量和字面宽度都对不上，13px 下整页发糊、发脏 ——
            // 这是「看着丑」比配色更直接的一个来源。
            //
            // 统一到「微软雅黑 UI」：Windows 上中文笔画清晰，Latin 部分来自
            // Segoe UI，和中文字形同源，混排时不再有两种质感。
            //
            // 这里只是「没有指定字体时用哪个」的兜底。设置页里可以换字体，
            // 换的时候改的是 Application.Resources 里的 DefaultFontFamily（正文控件）
            // 和 UiFontFamily（窗口级浮层）两个资源键，不需要重启。
            .With(new FontManagerOptions
            {
                DefaultFamilyName = "Microsoft YaHei UI",
                FontFallbacks = new[]
                {
                    new FontFallback { FontFamily = new FontFamily("Microsoft YaHei") },
                    new FontFallback { FontFamily = new FontFamily("Segoe UI") }
                }
            })
            .LogToTrace();
}
