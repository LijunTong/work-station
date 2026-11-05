namespace WorkStation.App.Services;

/// <summary>
/// 通知出口。抽成接口是为了让调度器能脱离窗口测试：
/// 无头测试注入一个记录器，就能验证「该弹的弹了、不该弹的没弹」。
/// </summary>
public interface INotificationSink
{
    /// <param name="onClick">点通知后要做的事。实现方负责在合适的时候调用它。</param>
    void Show(string title, string message, Action onClick);
}
