using System.Windows;
using System.Windows.Media;

namespace SysDash.Theming.Controls;

/// <summary>
/// Lightweight polyline sparkline. Values are normalized per-render (with an
/// optional fixed Maximum for metrics like percentages). Painted through
/// OnRender so it stays cheap at 1 Hz widget refresh rates.
///
/// Widget XAML should always set LineBrush/FillBrush via DynamicResource theme
/// brushes, which makes WPF re-evaluate the values and re-render on theme
/// switches. If the brushes are left unset, theme resources are looked up
/// directly (AccentBrush / AccentFillBrush / TrackBrush).
/// </summary>
public sealed class Sparkline : FrameworkElement
{
    public static readonly DependencyProperty ValuesProperty = DependencyProperty.Register(
        nameof(Values),
        typeof(IReadOnlyList<double>),
        typeof(Sparkline),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty LineBrushProperty = DependencyProperty.Register(
        nameof(LineBrush),
        typeof(Brush),
        typeof(Sparkline),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty FillBrushProperty = DependencyProperty.Register(
        nameof(FillBrush),
        typeof(Brush),
        typeof(Sparkline),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty GuideBrushProperty = DependencyProperty.Register(
        nameof(GuideBrush),
        typeof(Brush),
        typeof(Sparkline),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty StrokeThicknessProperty = DependencyProperty.Register(
        nameof(StrokeThickness),
        typeof(double),
        typeof(Sparkline),
        new FrameworkPropertyMetadata(1.6, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty MinimumProperty = DependencyProperty.Register(
        nameof(Minimum),
        typeof(double),
        typeof(Sparkline),
        new FrameworkPropertyMetadata(0d, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty MaximumProperty = DependencyProperty.Register(
        nameof(Maximum),
        typeof(double),
        typeof(Sparkline),
        new FrameworkPropertyMetadata(100d, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty AutoScaleProperty = DependencyProperty.Register(
        nameof(AutoScale),
        typeof(bool),
        typeof(Sparkline),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty ShowGuideLineProperty = DependencyProperty.Register(
        nameof(ShowGuideLine),
        typeof(bool),
        typeof(Sparkline),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty GuideValueProperty = DependencyProperty.Register(
        nameof(GuideValue),
        typeof(double),
        typeof(Sparkline),
        new FrameworkPropertyMetadata(100d, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>Values in chronological order. May be null/empty (draws nothing).</summary>
    public IReadOnlyList<double>? Values
    {
        get => (IReadOnlyList<double>?)GetValue(ValuesProperty);
        set => SetValue(ValuesProperty, value);
    }

    public Brush? LineBrush
    {
        get => (Brush?)GetValue(LineBrushProperty);
        set => SetValue(LineBrushProperty, value);
    }

    public Brush? FillBrush
    {
        get => (Brush?)GetValue(FillBrushProperty);
        set => SetValue(FillBrushProperty, value);
    }

    public Brush? GuideBrush
    {
        get => (Brush?)GetValue(GuideBrushProperty);
        set => SetValue(GuideBrushProperty, value);
    }

    public double StrokeThickness
    {
        get => (double)GetValue(StrokeThicknessProperty);
        set => SetValue(StrokeThicknessProperty, value);
    }

    /// <summary>Lower bound of the value axis (used when AutoScale is false).</summary>
    public double Minimum
    {
        get => (double)GetValue(MinimumProperty);
        set => SetValue(MinimumProperty, value);
    }

    /// <summary>Upper bound of the value axis (used when AutoScale is false).</summary>
    public double Maximum
    {
        get => (double)GetValue(MaximumProperty);
        set => SetValue(MaximumProperty, value);
    }

    /// <summary>Fit the axis to the min/max of the current values (best for rates).</summary>
    public bool AutoScale
    {
        get => (bool)GetValue(AutoScaleProperty);
        set => SetValue(AutoScaleProperty, value);
    }

    /// <summary>Draw a faint horizontal guide line (e.g. 100% reference).</summary>
    public bool ShowGuideLine
    {
        get => (bool)GetValue(ShowGuideLineProperty);
        set => SetValue(ShowGuideLineProperty, value);
    }

    public double GuideValue
    {
        get => (double)GetValue(GuideValueProperty);
        set => SetValue(GuideValueProperty, value);
    }

    protected override void OnRender(DrawingContext dc)
    {
        IReadOnlyList<double>? values = Values;
        if (values is null || values.Count == 0 || ActualWidth < 6 || ActualHeight < 6)
        {
            return;
        }

        Brush line = LineBrush ?? (Brush?)TryFindResource("AccentBrush") ?? Brushes.DodgerBlue;
        Brush? fill = FillBrush ?? (Brush?)TryFindResource("AccentFillBrush");
        Brush guide = GuideBrush ?? (Brush?)TryFindResource("TrackBrush") ?? Brushes.Gray;

        double min;
        double max;
        if (AutoScale)
        {
            double dataMin = double.MaxValue;
            double dataMax = double.MinValue;
            for (int i = 0; i < values.Count; i++)
            {
                if (values[i] < dataMin)
                {
                    dataMin = values[i];
                }

                if (values[i] > dataMax)
                {
                    dataMax = values[i];
                }
            }

            min = Math.Max(0, dataMin);
            max = Math.Max(dataMax, min + 1);
        }
        else
        {
            min = Minimum;
            max = Maximum;
            if (max <= min)
            {
                max = min + 1;
            }
        }

        double w = ActualWidth;
        double h = ActualHeight;
        double span = max - min;

        double Y(double v)
        {
            double clamped = Math.Clamp(v, min, max);
            return h - ((clamped - min) / span * h);
        }

        dc.PushClip(new RectangleGeometry(new Rect(0, 0, w, h)));
        try
        {
            if (ShowGuideLine)
            {
                double gy = Y(GuideValue);
                dc.DrawLine(new Pen(guide, 1.0), new Point(0, gy), new Point(w, gy));
            }

            int n = values.Count;
            var lineGeom = new StreamGeometry();
            using (StreamGeometryContext lg = lineGeom.Open())
            {
                lg.BeginFigure(new Point(0, Y(values[0])), false, false);
                for (int i = 1; i < n; i++)
                {
                    lg.LineTo(new Point(w * i / (n - 1), Y(values[i])), true, false);
                }
            }

            if (fill is not null && fill != Brushes.Transparent)
            {
                var area = new StreamGeometry();
                using (StreamGeometryContext ag = area.Open())
                {
                    ag.BeginFigure(new Point(0, h), true, false);
                    for (int i = 0; i < n; i++)
                    {
                        ag.LineTo(new Point(w * i / (n - 1), Y(values[i])), true, false);
                    }

                    ag.LineTo(new Point(w, h), true, false);
                    ag.LineTo(new Point(0, h), true, false);
                }

                dc.DrawGeometry(fill, null, area);
            }

            dc.DrawGeometry(line, new Pen(line, StrokeThickness), lineGeom);
        }
        finally
        {
            dc.Pop();
        }
    }
}