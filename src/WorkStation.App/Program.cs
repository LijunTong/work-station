using Avalonia;

namespace WorkStation.App;

internal static class Program
{
    // Avalonia 没有 App.xaml 的 StartupUri 惯例，入口必须显式构建 AppBuilder。
    // UsePlatformDetect 会按平台挑渲染后端：Windows=Direct2D/X11，macOS=Metal，Linux=X11/Wayland。
    [STAThread]
    public static void Main(string[] args) =>
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);

    // 由 Avalonia.Headless 的测试宿主复用，因此必须是 public 且无参可调。
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
