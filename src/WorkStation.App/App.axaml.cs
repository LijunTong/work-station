using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using WorkStation.App.ViewModels;

namespace WorkStation.App;

public partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            try
            {
                var services = AppServices.Start();
                desktop.ShutdownRequested += (_, _) => services.Dispose();
                desktop.MainWindow = new Views.MainWindow(new MainWindowViewModel(services));
            }
            catch (Exception error)
            {
                // 数据库打不开时不能静默退出：Window 里有一块错误页把原因显示出来。
                // 这里不再 throw —— 抛出去只会让进程无声消失。
                desktop.MainWindow = new Views.MainWindow(MainWindowViewModel.StartupFailed(error));
            }
        }

        base.OnFrameworkInitializationCompleted();
    }
}
