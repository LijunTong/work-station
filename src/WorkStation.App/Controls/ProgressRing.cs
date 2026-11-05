using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace WorkStation.App.Controls;

/// <summary>
/// 进度环。用一份 StreamGeometry 画弧，避免为每个实例引入图片资源。
///
/// 数值做了三重防护：非有限值按 0 处理、范围 clamp 到 0–100、100% 时直接画整圆
/// （起终点重合的 ArcTo 在 Skia 下画不出可见弧线）。
/// </summary>
public class ProgressRing : Control
{
    public static readonly StyledProperty<double> ValueProperty =
        AvaloniaProperty.Register<ProgressRing, double>(nameof(Value));

    public static readonly StyledProperty<IBrush?> RingBrushProperty =
        AvaloniaProperty.Register<ProgressRing, IBrush?>(nameof(RingBrush));

    public static readonly StyledProperty<IBrush?> TrackBrushProperty =
        AvaloniaProperty.Register<ProgressRing, IBrush?>(nameof(TrackBrush));

    public static readonly StyledProperty<double> ThicknessProperty =
        AvaloniaProperty.Register<ProgressRing, double>(nameof(Thickness), 4d);

    static ProgressRing()
    {
        AffectsRender<ProgressRing>(ValueProperty, RingBrushProperty, TrackBrushProperty, ThicknessProperty);
    }

    public double Value
    {
        get => GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    public IBrush? RingBrush
    {
        get => GetValue(RingBrushProperty);
        set => SetValue(RingBrushProperty, value);
    }

    public IBrush? TrackBrush
    {
        get => GetValue(TrackBrushProperty);
        set => SetValue(TrackBrushProperty, value);
    }

    public double Thickness
    {
        get => GetValue(ThicknessProperty);
        set => SetValue(ThicknessProperty, value);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var width = double.IsInfinity(availableSize.Width) ? 40 : availableSize.Width;
        var height = double.IsInfinity(availableSize.Height) ? 40 : availableSize.Height;
        return new Size(width, height);
    }

    public override void Render(DrawingContext context)
    {
        var size = Math.Min(Bounds.Width, Bounds.Height);
        if (size <= 0) return;

        var thickness = Math.Clamp(Thickness, 1, size / 2);
        var radius = size / 2 - thickness / 2;
        if (radius <= 0) return;

        var center = new Point(Bounds.Width / 2, Bounds.Height / 2);

        if (TrackBrush is { } track)
        {
            context.DrawEllipse(null, new Pen(track, thickness), center, radius, radius);
        }

        var value = double.IsFinite(Value) ? Math.Clamp(Value, 0, 100) : 0;
        if (value <= 0) return;

        var brush = RingBrush ?? Brushes.Transparent;
        // 圆头收尾：平头会让弧的末端看起来像被切了一刀，短进度时尤其明显
        var pen = new Pen(brush, thickness, null, PenLineCap.Round);

        if (value >= 100)
        {
            context.DrawEllipse(null, pen, center, radius, radius);
            return;
        }

        var startAngle = -Math.PI / 2;
        var endAngle = startAngle + value / 100 * Math.PI * 2;
        var start = new Point(
            center.X + radius * Math.Cos(startAngle),
            center.Y + radius * Math.Sin(startAngle));
        var end = new Point(
            center.X + radius * Math.Cos(endAngle),
            center.Y + radius * Math.Sin(endAngle));

        var geometry = new StreamGeometry();
        using (var stream = geometry.Open())
        {
            stream.BeginFigure(start, isFilled: false);
            stream.ArcTo(end, new Size(radius, radius), 0, value > 50, SweepDirection.Clockwise);
        }

        context.DrawGeometry(null, pen, geometry);
    }
}
