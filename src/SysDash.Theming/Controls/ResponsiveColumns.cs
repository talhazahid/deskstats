using System.Windows;
using System.Windows.Controls;

namespace SysDash.Theming.Controls;

/// <summary>
/// Fluid card layout for widgets. At the default width the sections stack in one
/// column; as the widget is made wider they flow into 2, then 3 columns
/// (masonry: each section goes into the currently shortest column, so the
/// reading order stays left-to-right, top-to-bottom and no gaps open up).
///
/// UX rules: a column is never narrower than <see cref="MinColumnWidth"/> (the
/// widget's natural single-column width), there are at most
/// <see cref="MaxColumns"/> columns, and never more columns than sections.
/// Sections marked <c>ResponsiveColumns.FullRow="True"</c> (e.g. a warning
/// banner) always span the full width.
/// </summary>
public sealed class ResponsiveColumns : Panel
{
    public static readonly DependencyProperty MinColumnWidthProperty = DependencyProperty.Register(
        nameof(MinColumnWidth), typeof(double), typeof(ResponsiveColumns),
        new FrameworkPropertyMetadata(264d, FrameworkPropertyMetadataOptions.AffectsMeasure));

    public static readonly DependencyProperty MaxColumnsProperty = DependencyProperty.Register(
        nameof(MaxColumns), typeof(int), typeof(ResponsiveColumns),
        new FrameworkPropertyMetadata(3, FrameworkPropertyMetadataOptions.AffectsMeasure));

    public static readonly DependencyProperty ColumnGapProperty = DependencyProperty.Register(
        nameof(ColumnGap), typeof(double), typeof(ResponsiveColumns),
        new FrameworkPropertyMetadata(8d, FrameworkPropertyMetadataOptions.AffectsMeasure));

    public static readonly DependencyProperty FullRowProperty = DependencyProperty.RegisterAttached(
        "FullRow", typeof(bool), typeof(ResponsiveColumns),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsParentMeasure));

    private readonly List<Rect> _slots = new();

    public double MinColumnWidth
    {
        get => (double)GetValue(MinColumnWidthProperty);
        set => SetValue(MinColumnWidthProperty, value);
    }

    public int MaxColumns
    {
        get => (int)GetValue(MaxColumnsProperty);
        set => SetValue(MaxColumnsProperty, value);
    }

    public double ColumnGap
    {
        get => (double)GetValue(ColumnGapProperty);
        set => SetValue(ColumnGapProperty, value);
    }

    public static bool GetFullRow(DependencyObject element) => (bool)element.GetValue(FullRowProperty);

    public static void SetFullRow(DependencyObject element, bool value) => element.SetValue(FullRowProperty, value);

    /// <summary>Columns used by the last layout pass.</summary>
    public int ColumnCount { get; private set; } = 1;

    /// <summary>The most columns this widget can use right now (visible sections, capped by <see cref="MaxColumns"/>).</summary>
    public int UsefulColumns => Math.Clamp(FlowingChildren(), 1, Math.Max(1, MaxColumns));

    protected override Size MeasureOverride(Size availableSize)
    {
        double width = double.IsInfinity(availableSize.Width) ? MinColumnWidth : availableSize.Width;
        double height = Layout(width, measure: true);
        return new Size(width, height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        if (_slots.Count == 0 || Math.Abs(_slots.Max(s => s.Right) - finalSize.Width) > 0.5)
        {
            Layout(finalSize.Width, measure: false);
        }

        int i = 0;
        foreach (UIElement child in InternalChildren)
        {
            if (child.Visibility == Visibility.Collapsed)
            {
                child.Arrange(new Rect(0, 0, 0, 0));
                continue;
            }

            if (i < _slots.Count)
            {
                child.Arrange(_slots[i]);
            }

            i++;
        }

        return finalSize;
    }

    private int FlowingChildren()
    {
        int count = 0;
        foreach (UIElement child in InternalChildren)
        {
            if (child.Visibility != Visibility.Collapsed && !GetFullRow(child))
            {
                count++;
            }
        }

        return count;
    }

    /// <summary>
    /// Places every visible child; returns the total height. With 2–3 columns the
    /// columns are then balanced: inside each band (the run of cards between
    /// full-width rows) shorter columns grow so every column ends at the same line —
    /// the extra height is shared by that column's cards (whose content then
    /// centres under its title, see <see cref="SectionLayout"/>), so no blank gap
    /// is left at the bottom.
    /// </summary>
    private double Layout(double width, bool measure)
    {
        _slots.Clear();
        double gap = Math.Max(0, ColumnGap);
        double minCol = Math.Max(80, MinColumnWidth);

        int fit = (int)Math.Floor((width + gap) / (minCol + gap));
        int columns = Math.Clamp(fit, 1, UsefulColumns);
        ColumnCount = columns;

        double columnWidth = Math.Max(1, (width - gap * (columns - 1)) / columns);
        var heights = new double[columns];
        var bandSlots = new List<(int Slot, int Column)>();

        void CloseBand()
        {
            if (columns < 2 || bandSlots.Count == 0)
            {
                bandSlots.Clear();
                return;
            }

            double target = heights.Max();
            for (int c = 0; c < columns; c++)
            {
                List<int> inColumn = bandSlots.Where(b => b.Column == c).Select(b => b.Slot).ToList();
                double extra = target - heights[c];
                if (inColumn.Count == 0 || extra < 0.5)
                {
                    continue;
                }

                double share = extra / inColumn.Count;
                double shift = 0;
                foreach (int slot in inColumn)
                {
                    Rect r = _slots[slot];
                    _slots[slot] = new Rect(r.X, r.Y + shift, r.Width, r.Height + share);
                    shift += share;
                }

                heights[c] = target;
            }

            bandSlots.Clear();
        }

        foreach (UIElement child in InternalChildren)
        {
            if (child.Visibility == Visibility.Collapsed)
            {
                if (measure)
                {
                    child.Measure(new Size(0, 0));
                }

                continue;
            }

            if (GetFullRow(child) || columns == 1)
            {
                CloseBand();
                double top = heights.Max();
                if (measure)
                {
                    child.Measure(new Size(width, double.PositiveInfinity));
                }

                double h = child.DesiredSize.Height;
                _slots.Add(new Rect(0, top, width, h));
                for (int c = 0; c < columns; c++)
                {
                    heights[c] = top + h;
                }

                continue;
            }

            int shortest = 0;
            for (int c = 1; c < columns; c++)
            {
                if (heights[c] < heights[shortest] - 0.5)
                {
                    shortest = c;
                }
            }

            if (measure)
            {
                child.Measure(new Size(columnWidth, double.PositiveInfinity));
            }

            double childHeight = child.DesiredSize.Height;
            bandSlots.Add((_slots.Count, shortest));
            _slots.Add(new Rect(shortest * (columnWidth + gap), heights[shortest], columnWidth, childHeight));
            heights[shortest] += childHeight;
        }

        CloseBand();
        return heights.Length == 0 ? 0 : heights.Max();
    }
}
