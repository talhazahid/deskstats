using System.Windows;
using System.Windows.Media;

namespace SysDash.Theming.Controls;

/// <summary>
/// History bar chart (iStat-style): one thin bar per sample, newest on the right.
/// With <see cref="SecondaryValues"/> set it becomes a mirrored chart — primary
/// bars grow up from a centre baseline and secondary bars grow down (upload /
/// download, read / write). Idle samples still draw a 1 px tick so the baseline
/// reads as a dotted line.
/// </summary>
public sealed class BarGraph : FrameworkElement
{
    public static readonly DependencyProperty ValuesProperty = DependencyProperty.Register(
        nameof(Values), typeof(double[]), typeof(BarGraph),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty SecondaryValuesProperty = DependencyProperty.Register(
        nameof(SecondaryValues), typeof(double[]), typeof(BarGraph),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>Fixed scale maximum (e.g. 100 for percentages). 0 = auto-scale to the data.</summary>
    public static readonly DependencyProperty MaximumProperty = DependencyProperty.Register(
        nameof(Maximum), typeof(double), typeof(BarGraph),
        new FrameworkPropertyMetadata(0d, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty BarBrushProperty = DependencyProperty.Register(
        nameof(BarBrush), typeof(Brush), typeof(BarGraph),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty SecondaryBrushProperty = DependencyProperty.Register(
        nameof(SecondaryBrush), typeof(Brush), typeof(BarGraph),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty BarWidthProperty = DependencyProperty.Register(
        nameof(BarWidth), typeof(double), typeof(BarGraph),
        new FrameworkPropertyMetadata(2.5d, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty GapProperty = DependencyProperty.Register(
        nameof(Gap), typeof(double), typeof(BarGraph),
        new FrameworkPropertyMetadata(1.5d, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>
    /// Number of samples the graph spans (the history size). When set, bars are
    /// sized so the whole span fits the width — a longer time span means more,
    /// thinner bars. 0 = fixed bar width (newest samples that fit).
    /// </summary>
    public static readonly DependencyProperty CapacityProperty = DependencyProperty.Register(
        nameof(Capacity), typeof(int), typeof(BarGraph),
        new FrameworkPropertyMetadata(0, FrameworkPropertyMetadataOptions.AffectsRender));

    public int Capacity
    {
        get => (int)GetValue(CapacityProperty);
        set => SetValue(CapacityProperty, value);
    }

    public double[]? Values
    {
        get => (double[]?)GetValue(ValuesProperty);
        set => SetValue(ValuesProperty, value);
    }

    public double[]? SecondaryValues
    {
        get => (double[]?)GetValue(SecondaryValuesProperty);
        set => SetValue(SecondaryValuesProperty, value);
    }

    public double Maximum
    {
        get => (double)GetValue(MaximumProperty);
        set => SetValue(MaximumProperty, value);
    }

    public Brush? BarBrush
    {
        get => (Brush?)GetValue(BarBrushProperty);
        set => SetValue(BarBrushProperty, value);
    }

    public Brush? SecondaryBrush
    {
        get => (Brush?)GetValue(SecondaryBrushProperty);
        set => SetValue(SecondaryBrushProperty, value);
    }

    public double BarWidth
    {
        get => (double)GetValue(BarWidthProperty);
        set => SetValue(BarWidthProperty, value);
    }

    public double Gap
    {
        get => (double)GetValue(GapProperty);
        set => SetValue(GapProperty, value);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        double width = double.IsInfinity(availableSize.Width) ? 200 : availableSize.Width;
        double height = double.IsInfinity(availableSize.Height) ? 60 : availableSize.Height;
        return new Size(width, height);
    }

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth;
        double h = ActualHeight;
        if (w < 4 || h < 4)
        {
            return;
        }

        // Transparent hit area so the whole graph is draggable/clickable.
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, w, h));

        double[] primary = Values ?? Array.Empty<double>();
        double[]? secondary = SecondaryValues;
        bool mirrored = secondary is not null;

        double max = Maximum;
        if (max <= 0)
        {
            max = 0;
            foreach (double v in primary)
            {
                max = Math.Max(max, v);
            }

            if (secondary is not null)
            {
                foreach (double v in secondary)
                {
                    max = Math.Max(max, v);
                }
            }

            if (max <= 0)
            {
                max = 1;
            }
        }

        Brush up = BarBrush ?? (Brush?)TryFindResource("AccentBrush") ?? Brushes.DodgerBlue;
        Brush down = SecondaryBrush ?? (Brush?)TryFindResource("SecondarySeriesBrush") ?? Brushes.Gray;

        double barWidth = Math.Max(1, BarWidth);
        double step = barWidth + Math.Max(0, Gap);
        int slots = Math.Max(1, (int)Math.Floor((w + Math.Max(0, Gap)) / step));

        if (Capacity > 0)
        {
            // Fit the whole time span, but never draw bars closer than MinStep px:
            // a long span on a narrow widget turned into a solid "wall" of 1 px
            // bars. Instead several samples share one bar (their peak), so the
            // time span stays the same and every bar stays readable.
            const double MinStep = 3.5;
            int maxSlots = Math.Max(1, (int)Math.Floor(w / MinStep));
            int perBar = Math.Max(1, (int)Math.Ceiling(Capacity / (double)maxSlots));
            slots = Math.Max(1, (int)Math.Ceiling(Capacity / (double)perBar));
            step = w / slots;
            barWidth = Math.Max(1, step * 0.6);

            if (perBar > 1)
            {
                primary = Downsample(primary, perBar);
                if (secondary is not null)
                {
                    secondary = Downsample(secondary, perBar);
                }
            }
        }

        double baseline = mirrored ? h / 2 : h;
        double span = mirrored ? h / 2 - 1 : h - 1;

        DrawSeries(dc, primary, slots, w, step, barWidth, baseline, span, max, up, upward: true);
        if (secondary is not null)
        {
            DrawSeries(dc, secondary, slots, w, step, barWidth, baseline, span, max, down, upward: false);
        }
    }

    /// <summary>Groups samples (newest group aligned to the right edge) and keeps each group's peak.</summary>
    private static double[] Downsample(double[] values, int perBar)
    {
        if (values.Length == 0 || perBar <= 1)
        {
            return values;
        }

        int groups = (int)Math.Ceiling(values.Length / (double)perBar);
        var result = new double[groups];
        int end = values.Length;
        for (int g = groups - 1; g >= 0; g--)
        {
            int start = Math.Max(0, end - perBar);
            double peak = 0;
            for (int i = start; i < end; i++)
            {
                double v = values[i];
                if (!double.IsNaN(v) && !double.IsInfinity(v) && v > peak)
                {
                    peak = v;
                }
            }

            result[g] = peak;
            end = start;
        }

        return result;
    }

    private static void DrawSeries(DrawingContext dc, double[] values, int slots, double width, double step,
        double barWidth, double baseline, double span, double max, Brush brush, bool upward)
    {
        int count = Math.Min(values.Length, slots);
        int first = values.Length - count;

        for (int i = 0; i < count; i++)
        {
            double v = values[first + i];
            if (double.IsNaN(v) || double.IsInfinity(v) || v < 0)
            {
                v = 0;
            }

            double barHeight = Math.Max(1, Math.Min(1, v / max) * span);
            // Newest sample sits at the right edge.
            double x = width - (count - i) * step + (step - barWidth);
            double y = upward ? baseline - barHeight : baseline;
            dc.DrawRectangle(brush, null, new Rect(x, y, barWidth, barHeight));
        }
    }
}
