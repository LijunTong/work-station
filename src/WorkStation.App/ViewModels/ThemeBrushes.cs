using Avalonia;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.Styling;

namespace WorkStation.App.ViewModels;

/// <summary>
/// ViewModel 里需要给数字、色条、状态点上色时，统一从这里取 SukiUI 的主题资源。
/// Suki 的键是 Color，而 Avalonia 的画刷属性要 IBrush，所以这里做一次转换；
/// 取资源时带 ActualThemeVariant，否则深色界面会拿到浅色字典的值。
/// 没有 Application（纯 ViewModel 单测）时退回系统中性灰，不引入任何自定义色值。
/// </summary>
internal static class ThemeBrushes
{
    public static IBrush Get(string sukiKey)
    {
        var application = Application.Current;

        // 纯 ViewModel 单测跑在线程池里；Application 的资源与 ActualThemeVariant
        // 属于 UI 线程，跨线程读会直接抛 InvalidOperationException。
        if (application is not null
            && Dispatcher.UIThread.CheckAccess()
            && application.TryGetResource(sukiKey, application.ActualThemeVariant, out var found))
        {
            if (found is IBrush brush) return brush;
            if (found is Color color) return new SolidColorBrush(color);
        }

        return Brushes.Gray;
    }

    /// <summary>
    /// 列底色：同一个 Suki 语义色压到低透明度。颜色来源仍是主题资源，
    /// 只在这里加一层 alpha，用于把四种状态列区分开而不压住卡片文字。
    /// </summary>
    public static IBrush Tint(string sukiKey, byte alpha = 26)
    {
        var color = Colors.Gray;
        var application = Application.Current;

        if (application is not null
            && Dispatcher.UIThread.CheckAccess()
            && application.TryGetResource(sukiKey, application.ActualThemeVariant, out var found))
        {
            if (found is Color c) color = c;
            else if (found is SolidColorBrush brush) color = brush.Color;
        }

        var isDark = application is not null
                     && Dispatcher.UIThread.CheckAccess()
                     && application.ActualThemeVariant == ThemeVariant.Dark;
        var effectiveAlpha = isDark ? (byte)46 : alpha;
        return new SolidColorBrush(new Color(effectiveAlpha, color.R, color.G, color.B));
    }
}
