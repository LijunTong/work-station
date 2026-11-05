using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using WorkStation.App.Controls;

namespace WorkStation.Tests;

/// <summary>
/// 日期字段：一个控件承担「日期 + 时刻」两件事。
///
/// 这里守的是显示口径 —— 值怎么拼成一行文字、没值时显示什么、
/// 只要日期时时间行是否真的收起来。方位与浮层交互靠手测。
/// </summary>
public class DueDateFieldTests
{
    [AvaloniaFact]
    public void DateAndTime_ShareOneLine()
    {
        var field = new DueDateField
        {
            SelectedDate = new DateTime(2026, 11, 5),
            SelectedTime = new TimeSpan(18, 30, 0)
        };

        Assert.Equal("11月5日 周四 18:30", Text(field));
    }

    [AvaloniaFact]
    public void EmptyValue_ShowsWatermark()
    {
        var field = new DueDateField();

        var text = field.FindControl<TextBlock>("ValueText");
        Assert.NotNull(text);
        Assert.Equal("不设置", text!.Text);
        Assert.Contains("placeholder", text.Classes);
    }

    [AvaloniaFact]
    public void CustomWatermark_IsUsed()
    {
        var field = new DueDateField { Watermark = "未排期" };

        Assert.Equal("未排期", Text(field));
    }

    [AvaloniaFact]
    public void DateOnly_HidesTimeRow()
    {
        var field = new DueDateField
        {
            ShowTime = false,
            SelectedDate = new DateTime(2026, 11, 5)
        };

        var row = field.FindControl<StackPanel>("TimeRow");
        Assert.NotNull(row);
        Assert.False(row!.IsVisible);

        // 只显示日期时不该带上时刻
        Assert.Equal("11月5日 周四", Text(field));
    }

    [AvaloniaFact]
    public void OtherYear_KeepsTheYear()
    {
        var field = new DueDateField
        {
            ShowTime = false,
            SelectedDate = new DateTime(2031, 1, 3)
        };

        Assert.Equal("2031年1月3日", Text(field));
    }

    private static string Text(DueDateField field)
    {
        var text = field.FindControl<TextBlock>("ValueText");
        Assert.NotNull(text);
        return text!.Text ?? string.Empty;
    }
}
