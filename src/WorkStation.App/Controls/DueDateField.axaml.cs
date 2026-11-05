using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Interactivity;
using Avalonia.Media;

namespace WorkStation.App.Controls;

/// <summary>
/// 截止日期 / 目标日期字段。
///
/// 为什么不用 <c>CalendarDatePicker</c> + <c>TimePicker</c>：那两颗控件各带一套 Fluent 模板，
/// 日期框右侧挂着一个带边框的整块日历按钮，时间框又只在有日期时才出现，
/// 于是抽屉里出现「一个宽框 + 一个方块 + 一个冒号框」三截长短不一的控件。
/// 这里合成一颗：图标 + 值 + 清除，浮层里同时给日历和时间，选择器只在需要时展开。
///
/// 值通过 <see cref="SelectedDate"/> / <see cref="SelectedTime"/> 与视图模型双向绑定，
/// 控件自己不做校验，也不碰存储格式。
/// </summary>
public partial class DueDateField : UserControl
{
    public static readonly StyledProperty<DateTime?> SelectedDateProperty =
        AvaloniaProperty.Register<DueDateField, DateTime?>(
            nameof(SelectedDate), defaultBindingMode: BindingMode.TwoWay);

    public static readonly StyledProperty<TimeSpan?> SelectedTimeProperty =
        AvaloniaProperty.Register<DueDateField, TimeSpan?>(
            nameof(SelectedTime), defaultBindingMode: BindingMode.TwoWay);

    /// <summary>只要日期（项目表单），不要时刻（任务表单）。</summary>
    public static readonly StyledProperty<bool> ShowTimeProperty =
        AvaloniaProperty.Register<DueDateField, bool>(nameof(ShowTime), true);

    public static readonly StyledProperty<string> WatermarkProperty =
        AvaloniaProperty.Register<DueDateField, string>(nameof(Watermark), "不设置");

    /// <summary>只挑日期时补的默认时刻：下班前，比 00:00 有意义。</summary>
    private static readonly TimeSpan DefaultTime = new(18, 0, 0);

    private static readonly string[] WeekdayNames = ["周日", "周一", "周二", "周三", "周四", "周五", "周六"];

    /// <summary>控件自己改值时置位，避免「设值 → 事件 → 再设值」的回环。</summary>
    private bool _syncing;

    public DueDateField()
    {
        InitializeComponent();

        // 用代码指定锚点：Popup 在 XAML 里按名字绑定 PlacementTarget 时，
        // 打开那一刻可能还没拿到目标的实际位置，浮层会跑到窗口顶部
        Pop.PlacementTarget = FieldButton;

        HourBox.ItemsSource = Enumerable.Range(0, 24).Select(h => h.ToString("00", CultureInfo.InvariantCulture)).ToList();
        MinuteBox.ItemsSource = Enumerable.Range(0, 60).Select(m => m.ToString("00", CultureInfo.InvariantCulture)).ToList();

        HourBox.SelectionChanged += OnTimePartChanged;
        MinuteBox.SelectionChanged += OnTimePartChanged;
        Cal.SelectedDatesChanged += OnCalendarSelectionChanged;

        SyncFromValue();
    }

    public DateTime? SelectedDate
    {
        get => GetValue(SelectedDateProperty);
        set => SetValue(SelectedDateProperty, value);
    }

    public TimeSpan? SelectedTime
    {
        get => GetValue(SelectedTimeProperty);
        set => SetValue(SelectedTimeProperty, value);
    }

    public bool ShowTime
    {
        get => GetValue(ShowTimeProperty);
        set => SetValue(ShowTimeProperty, value);
    }

    public string Watermark
    {
        get => GetValue(WatermarkProperty);
        set => SetValue(WatermarkProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == SelectedDateProperty
            || change.Property == SelectedTimeProperty
            || change.Property == ShowTimeProperty
            || change.Property == WatermarkProperty)
        {
            SyncFromValue();
        }
    }

    /// <summary>把两个值同步到「文字、日历、时间下拉」三处显示。</summary>
    private void SyncFromValue()
    {
        if (_syncing) return;

        _syncing = true;
        try
        {
            TimeRow.IsVisible = ShowTime;

            var date = SelectedDate;
            var time = SelectedTime;

            Cal.SelectedDate = date;
            if (date is { } display) Cal.DisplayDate = display;

            var hour = time?.Hours ?? DefaultTime.Hours;
            var minute = time?.Minutes ?? DefaultTime.Minutes;
            HourBox.SelectedIndex = hour;
            MinuteBox.SelectedIndex = minute;

            ClearButton.IsVisible = date is not null;

            if (date is { } value)
            {
                ValueText.Text = Format(value, ShowTime ? time : null);
                ValueText.Classes.Remove("placeholder");
            }
            else
            {
                ValueText.Text = Watermark;
                ValueText.Classes.Set("placeholder", true);
            }
        }
        finally
        {
            _syncing = false;
        }
    }

    private void OnFieldClick(object? sender, RoutedEventArgs e)
    {
        Pop.IsOpen = !Pop.IsOpen;
    }

    private void OnClearClick(object? sender, RoutedEventArgs e)
    {
        _syncing = true;
        try
        {
            SelectedDate = null;
            SelectedTime = null;
        }
        finally
        {
            _syncing = false;
        }

        SyncFromValue();
        Pop.IsOpen = false;
    }

    private void OnDoneClick(object? sender, RoutedEventArgs e) => Pop.IsOpen = false;

    private void OnTodayClick(object? sender, RoutedEventArgs e) => ApplyDate(DateTime.Today);
    private void OnTomorrowClick(object? sender, RoutedEventArgs e) => ApplyDate(DateTime.Today.AddDays(1));

    private void OnCalendarSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_syncing) return;

        if (Cal.SelectedDate is { } date)
        {
            ApplyDate(date.Date, keepOpen: true);
        }
        else
        {
            SelectedDate = null;
            SelectedTime = null;
        }
    }

    private void ApplyDate(DateTime date, bool keepOpen = false)
    {
        _syncing = true;
        try
        {
            SelectedDate = date.Date;
            if (ShowTime && SelectedTime is null) SelectedTime = DefaultTime;
        }
        finally
        {
            _syncing = false;
        }

        SyncFromValue();
        if (!keepOpen) Pop.IsOpen = false;
    }

    private void OnTimePartChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_syncing) return;
        if (HourBox.SelectedIndex < 0 || MinuteBox.SelectedIndex < 0) return;

        _syncing = true;
        try
        {
            SelectedTime = new TimeSpan(HourBox.SelectedIndex, MinuteBox.SelectedIndex, 0);

            // 只改时间不改日期：没日期时把「今天」补上，否则时刻存不住
            SelectedDate ??= DateTime.Today;
        }
        finally
        {
            _syncing = false;
        }

        SyncFromValue();
    }

    /// <summary>「11月5日 周三 18:00」。跨年时补上年份，避免把明年的日期读成今年。</summary>
    private static string Format(DateTime date, TimeSpan? time)
    {
        var text = date.Year == DateTime.Today.Year
            ? $"{date.Month}月{date.Day}日 {WeekdayNames[(int)date.DayOfWeek]}"
            : $"{date.Year}年{date.Month}月{date.Day}日";

        return time is { } value ? $"{text} {value.Hours:00}:{value.Minutes:00}" : text;
    }
}
