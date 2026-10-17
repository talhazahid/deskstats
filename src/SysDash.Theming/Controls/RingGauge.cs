using System.Windows;
using System.Windows.Media;

namespace SysDash.Theming.Controls;

/// <summary>
/// Circular progress ring (iStat-style gauge). Draws a full track circle and a
/// rounded arc for <see cref="Value"/> (0–<see cref="Maximum"/>) starting at 12
/// o'clock. Text is placed on top by the caller (e.g. a centred StackPanel in
/// the same Grid cell), so the control itself stays a cheap OnRender element.
/// </summary>
public sealed class RingGauge : FrameworkElement
{
    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(
        nameof(Value), typeof(double), typeof(RingGauge),
        new FrameworkPropertyMetadata(0d, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty MaximumProperty = DependencyProperty.Register(
        nameof(Maximum), typeof(double), typeof(RingGauge),
        new FrameworkPropertyMetadata(100d, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty ThicknessProperty = DependencyProperty.Register(
        nameof(Thickness), typeof(double), typeof(RingGauge),
        new FrameworkPropertyMetadata(6d, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty RingBrushProperty = DependencyProperty.Register(
        nameof(RingBrush), typeof(Brush), typeof(RingGauge),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty TrackBrushProperty = DependencyProperty.Register(
        nameof(TrackBrush), typeof(Brush), typeof(RingGauge),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>Optional second arc drawn after the first one (e.g. "compressed" memory).</summary>
    public static readonly DependencyProperty SecondaryValueProperty = DependencyProperty.Register(
        nameof(SecondaryValue), typeof(double), typeof(RingGauge),
        new FrameworkPropertyMetadata(0d, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty SecondaryBrushProperty = DependencyProperty.Register(
        nameof(SecondaryBrush), typeof(Brush), typeof(RingGauge),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public double Value
    {
        get => (double)GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    public double Maximum
    {
        get => (double)GetValue(MaximumProperty);
        set => SetValue(MaximumProperty, value);
    }

    public double Thickness
    {
        get => (double)GetValue(ThicknessProperty);
        set => SetValue(ThicknessProperty, value);
    }

    public Brush? RingBrush
    {
        get => (Brush?)GetValue(RingBrushProperty);
        set => SetValue(RingBrushProperty, value);
    }

    public Brush? TrackBrush
    {
        get => (Brush?)GetValue(TrackBrushProperty);
        set => SetValue(TrackBrushProperty, value);
    }

    public double SecondaryValue
    {
        get => (double)GetValue(SecondaryValueProperty);
        set => SetValue(SecondaryValueProperty, value);
    }

    public Brush? SecondaryBrush
    {
        get => (Brush?)GetValue(SecondaryBrushProperty);
        set => SetValue(SecondaryBrushProperty, value);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        // Square by default; explicit Width/Height win.
        double size = 60;
        if (!double.IsInfinity(availableSize.Width) && !double.IsInfinity(availableSize.Height))
        {
            size = Math.Min(availableSize.Width, availableSize.Height);
        }

        return new Size(size, size);
    }

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth;
        double h = ActualHeight;
        double thickness = Math.Max(1, Thickness);
        double radius = (Math.Min(w, h) - thickness) / 2;
        if (radius <= 1)
        {
            return;
        }

        var center = new Point(w / 2, h / 2);
        Brush track = TrackBrush ?? (Brush?)TryFindResource("TrackBrush") ?? Brushes.DimGray;
        Brush ring = RingBrush ?? (Brush?)TryFindResource("AccentBrush") ?? Brushes.DodgerBlue;

        dc.DrawEllipse(null, new Pen(track, thickness), center, radius, radius);

        double max = Maximum > 0 ? Maximum : 100;
        double primary = Math.Clamp(Value / max, 0, 1);
        DrawArc(dc, center, radius, thickness, 0, primary, ring);

        if (SecondaryValue > 0)
        {
            double secondary = Math.Clamp(SecondaryValue / max, 0, 1 - primary);
            Brush second = SecondaryBrush ?? (Brush?)TryFindResource("PinkBrush") ?? Brushes.HotPink;
            DrawArc(dc, center, radius, thickness, primary, secondary, second);
        }
    }

    private static void DrawArc(DrawingContext dc, Point center, double radius, double thickness,
        double startFraction, double sweepFraction, Brush brush)
    {
        if (sweepFraction <= 0.001)
        {
            return;
        }

        var pen = new Pen(brush, thickness)
        {
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round,
        };

        if (sweepFraction >= 0.999)
        {
            dc.DrawEllipse(null, pen, center, radius, radius);
            return;
        }

        double startAngle = -90 + startFraction * 360;
        double endAngle = startAngle + sweepFraction * 360;
        Point start = PointOn(center, radius, startAngle);
        Point end = PointOn(center, radius, endAngle);

        var geometry = new StreamGeometry();
        using (StreamGeometryContext ctx = geometry.Open())
        {
            ctx.BeginFigure(start, false, false);
            ctx.ArcTo(end, new Size(radius, radius), 0, sweepFraction > 0.5, SweepDirection.Clockwise, true, false);
        }

        geometry.Freeze();
        dc.DrawGeometry(null, pen, geometry);
    }

    private static Point PointOn(Point center, double radius, double angleDegrees)
    {
        double rad = angleDegrees * Math.PI / 180;
        return new Point(center.X + radius * Math.Cos(rad), center.Y + radius * Math.Sin(rad));
    }
}
