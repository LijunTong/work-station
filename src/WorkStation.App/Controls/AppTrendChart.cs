using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using Avalonia.Media;

namespace WorkStation.App.Controls;

/// <summary>
/// 趋势图里的一天：到期数、完成数与日期标签。
///
/// 比例由 Core 算好（完成柱与到期柱各自按自己的最大值归一），
/// 控件只负责按可用高度把比例换算成像素。
/// </summary>
public sealed record AppTrendBar(
    string Label,
    int Completed,
    int Due,
    double CompletedRatio,
    double DueRatio,
    bool IsToday = false);

/// <summary>
/// 仪表盘的「近 7 天到期与完成」柱状图。
///
/// 为什么自己画而不用 ItemsControl + 固定高度：
/// 柱子的高度必须跟着**可用高度**走。左边的图表卡要和右边的两张分布卡一样高
/// （用户要求「高度拉伸到底部」），柱高写死 92px 的话，卡片拉高了柱子却还是那么点，
/// 底下留一大片空白，看起来就是没拉。
///
/// 这里在布局时按实际可用高度换算柱高：高度给多少，柱子就长多少。
/// </summary>
public sealed class AppTrendChart : Panel
{
    /// <summary>没有明确高度时（自动测量的场合）的默认绘图高度。</summary>
    private const double DefaultHeight = 150;

    private const double CountHeight = 16;
    private const double LabelHeight = 15;
    private const double LineGap = 3;

    public static readonly StyledProperty<IReadOnlyList<AppTrendBar>?> ItemsProperty =
        AvaloniaProperty.Register<AppTrendChart, IReadOnlyList<AppTrendBar>?>(nameof(Items));

    /// <summary>每天的柱子（可多天）。集合内容变化时图表会自己重建。</summary>
    public IReadOnlyList<AppTrendBar>? Items
    {
        get => GetValue(ItemsProperty);
        set => SetValue(ItemsProperty, value);
    }

    public static readonly StyledProperty<IBrush?> PrimaryBrushProperty =
        AvaloniaProperty.Register<AppTrendChart, IBrush?>(nameof(PrimaryBrush));

    /// <summary>完成柱的颜色。视图里用 <c>{DynamicResource SukiPrimaryColor}</c> 绑定。</summary>
    public IBrush? PrimaryBrush
    {
        get => GetValue(PrimaryBrushProperty);
        set => SetValue(PrimaryBrushProperty, value);
    }

    public static readonly StyledProperty<IBrush?> MutedBrushProperty =
        AvaloniaProperty.Register<AppTrendChart, IBrush?>(nameof(MutedBrush));

    /// <summary>到期柱与日期标签的颜色（弱化色）。</summary>
    public IBrush? MutedBrush
    {
        get => GetValue(MutedBrushProperty);
        set => SetValue(MutedBrushProperty, value);
    }

    public static readonly StyledProperty<double> BarWidthProperty =
        AvaloniaProperty.Register<AppTrendChart, double>(nameof(BarWidth), 12d);

    /// <summary>单根柱子的宽度（像素）。</summary>
    public double BarWidth
    {
        get => GetValue(BarWidthProperty);
        set => SetValue(BarWidthProperty, value);
    }

    public static readonly StyledProperty<double> BarGapProperty =
        AvaloniaProperty.Register<AppTrendChart, double>(nameof(BarGap), 4d);

    /// <summary>两根柱子之间的间距（像素）。</summary>
    public double BarGap
    {
        get => GetValue(BarGapProperty);
        set => SetValue(BarGapProperty, value);
    }

    private readonly List<Column> _columns = [];
    private Border? _baseline;
    private INotifyCollectionChanged? _observed;

    public AppTrendChart()
    {
        // 颜色由视图用 DynamicResource 绑进来；这里只给个中性底色兜底。
        PrimaryBrush = new SolidColorBrush(Color.FromRgb(0x0A, 0x59, 0xF7));
        MutedBrush = new SolidColorBrush(Color.FromRgb(0xC8, 0xCC, 0xD4));

        this.GetObservable(ItemsProperty)
            .Subscribe(new AnonymousObserver<IReadOnlyList<AppTrendBar>?>(OnItemsChanged));
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        // 主题切换后颜色会重算，把新颜色刷到已有控件上
        if (change.Property == PrimaryBrushProperty || change.Property == MutedBrushProperty)
        {
            ApplyBrushes();
        }
    }

    private void OnItemsChanged(IReadOnlyList<AppTrendBar>? items)
    {
        if (_observed is not null)
        {
            _observed.CollectionChanged -= OnCollectionChanged;
            _observed = null;
        }

        if (items is INotifyCollectionChanged observable)
        {
            observable.CollectionChanged += OnCollectionChanged;
            _observed = observable;
        }

        Rebuild();
    }

    private void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) => Rebuild();

    private void Rebuild()
    {
        Children.Clear();
        _columns.Clear();

        var items = Items;
        if (items is null) return;

        // 基线：没有它，几根短柱子会像浮在半空，看不出"从零长起来"
        _baseline = new Border
        {
            Background = MutedBrush,
            Opacity = 0.28,
            Height = 1
        };
        Children.Add(_baseline);

        foreach (var item in items)
        {
            var dueBar = new Border
            {
                Background = MutedBrush,
                Opacity = 0.35,
                CornerRadius = new CornerRadius(3, 3, 0, 0)
            };

            var doneBar = new Border
            {
                Background = PrimaryBrush,
                CornerRadius = new CornerRadius(3, 3, 0, 0)
            };

            // 0 不画数字：七格里五格是「0」，一排零比柱子还抢眼
            var countText = new TextBlock
            {
                Text = item.Completed > 0 ? item.Completed.ToString() : string.Empty,
                FontSize = 12,
                FontWeight = FontWeight.SemiBold,
                Foreground = PrimaryBrush,
                TextAlignment = TextAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };

            var labelText = new TextBlock
            {
                Text = item.Label,
                FontSize = 11,
                Foreground = MutedBrush,
                TextAlignment = TextAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };

            // 今天高亮一点：日期用主色，免得在七天里找不着「今天」
            if (item.IsToday)
            {
                labelText.FontWeight = FontWeight.SemiBold;
                labelText.Foreground = PrimaryBrush;
            }

            // 整列的悬停命中层：柱子只有十几像素宽，鼠标很难停在柱子上，
            // 所以让「这一天」的整列（含上下留白）都能触发提示。
            var hover = new Border { Background = Brushes.Transparent };
            ToolTip.SetTip(hover, $"{item.Label}　完成 {item.Completed}　到期 {item.Due}");
            ToolTip.SetShowDelay(hover, 120);
            ToolTip.SetPlacement(hover, PlacementMode.Top);

            Children.Add(dueBar);
            Children.Add(doneBar);
            Children.Add(countText);
            Children.Add(labelText);
            Children.Add(hover);

            _columns.Add(new Column(item, dueBar, doneBar, countText, labelText, hover));
        }

        InvalidateMeasure();
        InvalidateArrange();
    }

    private void ApplyBrushes()
    {
        if (_baseline is not null) _baseline.Background = MutedBrush;

        foreach (var column in _columns)
        {
            column.DueBar.Background = MutedBrush;
            column.DoneBar.Background = PrimaryBrush;
            column.CountText.Foreground = PrimaryBrush;
            column.LabelText.Foreground = column.Item.IsToday ? PrimaryBrush : MutedBrush;
        }
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        _baseline?.Measure(availableSize);

        foreach (var column in _columns)
        {
            column.DueBar.Measure(availableSize);
            column.DoneBar.Measure(availableSize);
            column.CountText.Measure(availableSize);
            column.LabelText.Measure(availableSize);
            column.Hover.Measure(availableSize);
        }

        // 高度给多少就要多少：卡片被拉高时图表跟着长满，而不是留一圈空白
        var width = double.IsFinite(availableSize.Width) ? availableSize.Width : 0;
        var height = double.IsFinite(availableSize.Height) ? availableSize.Height : DefaultHeight;

        return new Size(width, height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var count = _columns.Count;
        if (count == 0) return finalSize;

        var columnWidth = finalSize.Width / count;

        // 底下两行是数字与日期，剩下的是绘图区
        var chartHeight = Math.Max(
            0,
            finalSize.Height - CountHeight - LabelHeight - LineGap * 2);

        _baseline?.Arrange(new Rect(0, chartHeight, finalSize.Width, 1));

        // 柱宽跟着列宽走：固定 12px 在一列 70 多像素的格子里显得又细又散，
        // 七根柱子撑不起整张图，看起来就像"没画完"。
        var barWidth = Math.Clamp(columnWidth * 0.15, BarWidth * 0.6, BarWidth * 1.6);
        var barGap = BarGap;
        var totalBarWidth = barWidth * 2 + barGap;

        for (var i = 0; i < count; i++)
        {
            var column = _columns[i];
            var x = i * columnWidth;
            var barX = x + (columnWidth - totalBarWidth) / 2;

            // 比例由 Core 算好，这里只按绘图区高度换算成像素
            var dueHeight = chartHeight * Math.Clamp(column.Item.DueRatio, 0d, 1d);
            var doneHeight = chartHeight * Math.Clamp(column.Item.CompletedRatio, 0d, 1d);

            column.DueBar.Arrange(new Rect(
                barX,
                chartHeight - dueHeight,
                barWidth,
                dueHeight));

            column.DoneBar.Arrange(new Rect(
                barX + barWidth + barGap,
                chartHeight - doneHeight,
                barWidth,
                doneHeight));

            column.CountText.Arrange(new Rect(
                x,
                chartHeight + LineGap,
                columnWidth,
                CountHeight));

            column.LabelText.Arrange(new Rect(
                x,
                chartHeight + LineGap + CountHeight + LineGap,
                columnWidth,
                LabelHeight));

            // 命中层盖住整列（绘图区 + 数字 + 日期），鼠标落在这一列的任意高度都能出提示
            column.Hover.Arrange(new Rect(x, 0, columnWidth, finalSize.Height));
        }

        return finalSize;
    }

    /// <summary>一天对应的四个可视元素。</summary>
    private sealed record Column(
        AppTrendBar Item,
        Border DueBar,
        Border DoneBar,
        TextBlock CountText,
        TextBlock LabelText,
        Border Hover);

    /// <summary>只关心 OnNext 的最小观察者。Avalonia 的 Subscribe 扩展要 IObserver，这里不必引 Reactive。</summary>
    private sealed class AnonymousObserver<T> : IObserver<T>
    {
        private readonly Action<T> _onNext;

        public AnonymousObserver(Action<T> onNext) => _onNext = onNext;

        public void OnCompleted()
        {
        }

        public void OnError(Exception error)
        {
        }

        public void OnNext(T value) => _onNext(value);
    }
}
