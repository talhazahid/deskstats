using System.Windows;

namespace SysDash.Theming.Native;

/// <summary>Monitor-relative placement avoids ambiguous global DIPs on mixed-DPI desktops.</summary>
public static class FloatingPlacement
{
    public const double Gap = 20;
    public const double WidgetInset = 10;
    public const double Margin = 16;
    public static (Rect[] Bounds, double Scale) FitWithin(Rect work, IReadOnlyList<Size> sizes, IReadOnlyList<Point> preferred)
    {
        if (sizes.Count == 0) return (Array.Empty<Rect>(), 1);
        Rect area = work;
        area.Inflate(-Margin, -Margin);
        bool Pack(double scale, out Rect[] bounds)
        {
            // Window chrome already contributes an inset on each side. The
            // requested 20px gap is between visible cards, not transparent HWND edges.
            double spacing = Math.Max(0, Gap - 2 * WidgetInset * scale);
            bounds = new Rect[sizes.Count];
            // Fill adjacent columns rather than retaining scattered saved positions.
            var columns = new List<List<(int Index, double Width, double Height)>>();
            var heights = new List<double>();
            foreach (int i in Enumerable.Range(0, sizes.Count).OrderByDescending(i => sizes[i].Height))
            {
                double width = Math.Ceiling(sizes[i].Width * scale);
                double height = Math.Ceiling(sizes[i].Height * scale);
                if (width > area.Width || height > area.Height) return false;
                int column = -1;
                double bestRemaining = double.PositiveInfinity;
                for (int c = 0; c < columns.Count; c++)
                {
                    double remaining = area.Height - heights[c] - spacing - height;
                    if (remaining >= 0 && remaining < bestRemaining)
                    {
                        bestRemaining = remaining;
                        column = c;
                    }
                }
                if (column < 0)
                {
                    column = columns.Count;
                    columns.Add(new());
                    heights.Add(0);
                }
                if (columns[column].Count > 0) heights[column] += spacing;
                columns[column].Add((i, width, height));
                heights[column] += height;
            }
            double totalWidth = columns.Sum(c => c.Max(item => item.Width)) + spacing * (columns.Count - 1);
            if (totalWidth > area.Width) return false;
            double right = area.Right;
            foreach (var column in columns)
            {
                double columnWidth = column.Max(item => item.Width);
                double left = right - columnWidth;
                double top = area.Top;
                foreach (var item in column)
                {
                    bounds[item.Index] = new Rect(left, top, item.Width, item.Height);
                    top += item.Height + spacing;
                }
                right = left - spacing;
            }
            return true;
        }
        // Keep normal size whenever possible; reduce the complete group only as needed.
        double fit = 1;
        while (!Pack(fit, out _) && fit > 0.01) fit *= 0.9;
        Pack(fit, out Rect[] result);
        return (result, fit);
    }
    public static Rect PixelWorkArea(MonitorDescriptor monitor)
        => new(monitor.WorkAreaDip.X * monitor.DpiX / 96.0, monitor.WorkAreaDip.Y * monitor.DpiY / 96.0,
            monitor.WorkAreaDip.Width * monitor.DpiX / 96.0, monitor.WorkAreaDip.Height * monitor.DpiY / 96.0);

    public static Point RestorePixels(MonitorDescriptor monitor, double offsetX, double offsetY, double width, double height)
    {
        Rect work = monitor.WorkAreaDip;
        // The complete widget stays inside the work area, clear of the taskbar.
        double x = Math.Clamp(offsetX, Margin, Math.Max(Margin, work.Width - width - Margin));
        double y = Math.Clamp(offsetY, Margin, Math.Max(Margin, work.Height - height - Margin));
        return new Point((work.X + x) * monitor.DpiX / 96.0, (work.Y + y) * monitor.DpiY / 96.0);
    }

    public static (Rect Bounds, double HeightLimit)[] ArrangeInitial(Rect work, IReadOnlyList<Size> sizes)
    {
        if (sizes.Count == 0) return Array.Empty<(Rect, double)>();
        int columns = Math.Min(sizes.Count, Math.Min(3, Math.Max(1, (int)((work.Width - 2 * Margin + Gap) / (220 + Gap)))));
        int rows = (int)Math.Ceiling(sizes.Count / (double)columns);
        double width = Math.Min(320, Math.Max(40, (work.Width - 2 * Margin - (columns - 1) * Gap) / columns));
        var result = new (Rect, double)[sizes.Count];
        for (int column = 0; column < columns; column++)
        {
            double y = work.Top + Margin;
            for (int row = 0; row < rows; row++)
            {
                int i = column * rows + row;
                if (i >= sizes.Count) break;
                // Preserve full content height even when a column extends below the screen.
                double height = Math.Max(24, sizes[i].Height);
                result[i] = (new Rect(work.Right - Margin - width - column * (width + Gap), y, width, height), 0);
                y += height + Gap;
            }
        }
        return result;
    }
}
