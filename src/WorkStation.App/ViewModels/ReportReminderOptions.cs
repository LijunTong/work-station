namespace WorkStation.App.ViewModels;

/// <summary>
/// 周报提醒的星期选项。Value 是 ISO 星期几：1=周一 … 7=周日。
///
/// 做成 class 而不是 record：下拉框按引用记选中项，值相等的两个实例
/// 在它眼里等于「没换过」，选中态就不会重新渲染。
/// </summary>
public sealed class ReportWeekdayOption
{
    public ReportWeekdayOption(int value, string label)
    {
        Value = value;
        Label = label;
    }

    public int Value { get; }

    public string Label { get; }

    public override string ToString() => Label;
}

/// <summary>月报提醒的几号选项。</summary>
public sealed class ReportMonthDayOption
{
    public ReportMonthDayOption(int value, string label)
    {
        Value = value;
        Label = label;
    }

    public int Value { get; }

    public string Label { get; }

    public override string ToString() => Label;
}
