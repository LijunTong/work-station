using Avalonia;
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
            .WithInterFont()
            .LogToTrace();
}
