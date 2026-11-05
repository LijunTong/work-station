using Avalonia;
using Avalonia.Headless;

[assembly: AvaloniaTestApplication(typeof(WorkStation.Tests.TestAppBuilder))]

namespace WorkStation.Tests;

/// <summary>无头 Avalonia 宿主。加载真实的 App.axaml 与样式，XAML 写错会在这里直接失败。</summary>
public static class TestAppBuilder
{
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<WorkStation.App.App>()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions());
}
