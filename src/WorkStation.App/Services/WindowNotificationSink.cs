using Avalonia.Controls;
using Avalonia.Controls.Notifications;

namespace WorkStation.App.Services;

/// <summary>
/// 应用内通知：复用 Avalonia 的 <see cref="WindowNotificationManager"/>，浮在窗口右下角。
///
/// 为什么不上系统级 Toast：Windows 的 Toast 需要 AUMID 注册与打包身份，
/// 单文件未打包 exe 上会静默失败；应用内通知在三个平台行为一致，
/// 托盘图标同时给出「有几条没处理」的提示，够用且不引第三方包。
/// </summary>
public sealed class WindowNotificationSink : INotificationSink
{
    private readonly WindowNotificationManager _manager;

    public WindowNotificationSink(TopLevel topLevel)
    {
        _manager = new WindowNotificationManager(topLevel)
        {
            Position = NotificationPosition.BottomRight,
            MaxItems = 3
        };
    }

    public void Show(string title, string message, Action onClick) =>
        _manager.Show(new Notification(
            title,
            message,
            NotificationType.Information,
            TimeSpan.FromSeconds(10),
            onClick));
}
