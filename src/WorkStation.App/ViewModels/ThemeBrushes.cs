using Avalonia;
using Avalonia.Media;

namespace WorkStation.App.ViewModels;

/// <summary>
/// 视图模型里偶尔需要把「主题色」当成画刷用（进度环、概览数字）。
///
/// 关键点：取资源时必须带 <see cref="Application.ActualThemeVariant"/>。
/// 传 null 会落到默认（浅色）主题字典 —— 深色界面上的数字会取到近黑色，直接看不见。
/// 纯 ViewModel 单测里没有 Application，回落到写死的中间调。
/// </summary>
internal static class ThemeBrushes
{
    public static IBrush Get(string key, string fallback)
    {
        var application = Application.Current;

        if (application is not null
            && application.TryGetResource(key, application.ActualThemeVariant, out var found)
            && found is IBrush brush)
        {
            return brush;
        }

        return Brush.Parse(fallback);
    }
}
