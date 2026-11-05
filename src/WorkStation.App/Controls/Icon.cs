using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;

namespace WorkStation.App.Controls;

/// <summary>图标语义。名字描述「做什么」，不描述长相，换画法时不用改视图。</summary>
public enum IconKind
{
    Edit,
    Trash,
    Archive,
    Unarchive,
    ArrowRight,
    Clock,
    Close,
    Plus
}

/// <summary>
/// 16×16 网格上的线性图标，描边绘制。
///
/// 颜色不单独设：直接取继承下来的 <see cref="TextElement.ForegroundProperty"/>，
/// 所以放在按钮里时，按钮的 <c>Foreground</c>（含 pointerover / dangerish 的换色）
/// 会自动传导到图标上，不需要给每个图标再绑一次画刷。
/// </summary>
public sealed class Icon : Control
{
    public static readonly StyledProperty<IconKind> KindProperty =
        AvaloniaProperty.Register<Icon, IconKind>(nameof(Kind));

    public static readonly StyledProperty<double> SizeProperty =
        AvaloniaProperty.Register<Icon, double>(nameof(Size), 14d);

    private const double Grid = 16d;

    static Icon()
    {
        AffectsRender<Icon>(KindProperty, SizeProperty);
    }

    public IconKind Kind
    {
        get => GetValue(KindProperty);
        set => SetValue(KindProperty, value);
    }

    /// <summary>正方形边长，默认 14；行内小按钮用 13。</summary>
    public double Size
    {
        get => GetValue(SizeProperty);
        set => SetValue(SizeProperty, value);
    }

    protected override Size MeasureOverride(Size availableSize) =>
        new(Size, Size);

    public override void Render(DrawingContext context)
    {
        var brush = TextElement.GetForeground(this) ?? Brushes.Black;
        var pen = new Pen(brush, 1.5, null, PenLineCap.Round, PenLineJoin.Round);

        using var _ = context.PushTransform(Matrix.CreateScale(Size / Grid, Size / Grid));

        switch (Kind)
        {
            case IconKind.Edit:
                Stroke(context, pen, "M2.6 13.4 L3.4 10.2 L10.6 3 L13 5.4 L5.8 12.6 Z");
                break;

            case IconKind.Trash:
                Line(context, pen, 2.6, 4.6, 13.4, 4.6);
                Stroke(context, pen, "M6.4 4.6 L6.4 2.8 L9.6 2.8 L9.6 4.6");
                Stroke(context, pen, "M4.4 4.6 L5.1 12.9 Q5.2 13.7 6 13.7 L10 13.7 Q10.8 13.7 10.9 12.9 L11.6 4.6");
                break;

            case IconKind.Archive:
                Box(context, pen);
                Line(context, pen, 8, 7.4, 8, 10.9);
                Stroke(context, pen, "M6.3 9.2 L8 10.9 L9.7 9.2");
                break;

            case IconKind.Unarchive:
                Box(context, pen);
                Line(context, pen, 8, 10.9, 8, 7.4);
                Stroke(context, pen, "M6.3 9.1 L8 7.4 L9.7 9.1");
                break;

            case IconKind.ArrowRight:
                Line(context, pen, 2.8, 8, 13, 8);
                Stroke(context, pen, "M9.2 4.2 L13 8 L9.2 11.8");
                break;

            case IconKind.Clock:
                context.DrawEllipse(null, pen, new Point(8, 8), 5.7, 5.7);
                Stroke(context, pen, "M8 4.8 L8 8.3 L10.5 9.7");
                break;

            case IconKind.Close:
                Line(context, pen, 4.2, 4.2, 11.8, 11.8);
                Line(context, pen, 11.8, 4.2, 4.2, 11.8);
                break;

            case IconKind.Plus:
                Line(context, pen, 8, 3.6, 8, 12.4);
                Line(context, pen, 3.6, 8, 12.4, 8);
                break;
        }
    }

    /// <summary>归档类图标的共用底座：一条独立的盖板 + 一个开口箱体。
    /// 盖板和箱体必须留 1.8 的缝，否则 13px 下两个矩形会糊成「回」字。</summary>
    private static void Box(DrawingContext context, Pen pen)
    {
        Line(context, pen, 2.8, 3.4, 13.2, 3.4);
        Stroke(context, pen, "M3.6 5.2 L3.6 12.3 Q3.6 13.3 4.6 13.3 L11.4 13.3 Q12.4 13.3 12.4 12.3 L12.4 5.2");
    }

    private static void Line(DrawingContext context, Pen pen, double x1, double y1, double x2, double y2) =>
        context.DrawLine(pen, new Point(x1, y1), new Point(x2, y2));

    private static void Stroke(DrawingContext context, Pen pen, string path) =>
        context.DrawGeometry(null, pen, StreamGeometry.Parse(path));
}
