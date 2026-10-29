using System.Windows;

namespace SysDash.Theming.Native;

/// <summary>Monitor-relative placement avoids ambiguous global DIPs on mixed-DPI desktops.</summary>
public static class FloatingPlacement
{
    public const double Gap = 20;
    public const double WidgetInset = 10;
    public const double Margin = 16;
    public static (Rect Bounds, double Scale) FitDropped(Rect work, Size size, Point wanted,
        IReadOnlyList<Rect> fixedCards, Thickness padding, double currentScale = 1)
    {
        Rect area = work; area.Inflate(-Margin, -Margin);
        double gapX = Math.Clamp(padding.Left, 0, 100) + Math.Clamp(padding.Right, 0, 100);
        double gapY = Math.Clamp(padding.Top, 0, 100) + Math.Clamp(padding.Bottom, 0, 100);
        for (double factor = 1; factor >= 0.5; factor *= 0.9)
        {
            double width = Math.Ceiling(size.Width * factor), height = Math.Ceiling(size.Height * factor);
            if (width > area.Width || height > area.Height) continue;
            double inset = WidgetInset * currentScale * factor;
            Point preferred = new(Math.Clamp(wanted.X, area.Left, area.Right - width), Math.Clamp(wanted.Y, area.Top, area.Bottom - height));
            bool Free(Rect candidate)
            {
                if (!area.Contains(candidate)) return false;
                candidate.Inflate(-inset, -inset);
                return fixedCards.All(card =>
                {
                    card.Inflate(Math.Max(0, gapX - 0.25), Math.Max(0, gapY - 0.25));
                    return !card.IntersectsWith(candidate);
                });
            }
            Rect chosen = new(preferred, new Size(width, height));
            if (Free(chosen)) return (chosen, factor);
            var xs = fixedCards.SelectMany(r => new[] { r.Left - gapX - width + inset, r.Right + gapX - inset })
                .Append(preferred.X).Append(area.Left).Append(area.Right - width).Distinct();
            var ys = fixedCards.SelectMany(r => new[] { r.Top - gapY - height + inset, r.Bottom + gapY - inset })
                .Append(preferred.Y).Append(area.Top).Append(area.Bottom - height).Distinct();
            var candidates = (from x in xs from y in ys select new Rect(x, y, width, height))
                .Where(Free).OrderBy(r => (r.TopLeft - preferred).LengthSquared).ToArray();
            if (candidates.Length > 0) return (candidates[0], factor);
        }
        return (Rect.Empty, 1);
    }
    public static MonitorDescriptor? MonitorForPointer(IReadOnlyList<MonitorDescriptor> monitors, Point pixelPointer)
        => monitors.FirstOrDefault(m => new Rect(m.BoundsDip.X * m.DpiX / 96.0,
            m.BoundsDip.Y * m.DpiY / 96.0, m.PixelWidth, m.PixelHeight).Contains(pixelPointer));
    public static (Rect[] Bounds, double Scale) FitWithin(Rect work, IReadOnlyList<Size> sizes, IReadOnlyList<Point> preferred, Thickness? padding = null, IReadOnlyList<bool>? manual = null)
    {
        if (sizes.Count == 0) return (Array.Empty<Rect>(), 1);
        Rect area = work;
        area.Inflate(-Margin, -Margin);
        Thickness edges = padding ?? new Thickness(Gap / 2);
        double horizontalGap = Math.Clamp(edges.Left, 0, 100) + Math.Clamp(edges.Right, 0, 100);
        double verticalGap = Math.Clamp(edges.Top, 0, 100) + Math.Clamp(edges.Bottom, 0, 100);
        bool Pack(double scale, out Rect[] bounds)
        {
            // Window chrome already contributes an inset on each side. The
            // requested 20px gap is between visible cards, not transparent HWND edges.
            double horizontalSpacing = horizontalGap - 2 * WidgetInset * scale;
            double verticalSpacing = verticalGap - 2 * WidgetInset * scale;
            bounds = new Rect[sizes.Count];
            if (manual?.Any(value => value) == true)
            {
                var occupied = new List<Rect>();
                foreach (int i in Enumerable.Range(0, sizes.Count).OrderByDescending(i => manual[i]).ThenByDescending(i => sizes[i].Height))
                {
                    double width = Math.Ceiling(sizes[i].Width * scale), height = Math.Ceiling(sizes[i].Height * scale);
                    if (width > area.Width || height > area.Height) return false;
                    Rect Visible(Rect r) { r.Inflate(-WidgetInset * scale, -WidgetInset * scale); return r; }
                    bool Free(Rect r) => area.Contains(r) && occupied.All(other =>
                    {
                        other = Visible(other);
                        other.Inflate(Math.Max(0, horizontalGap - 0.1), Math.Max(0, verticalGap - 0.1));
                        return !other.IntersectsWith(Visible(r));
                    });
                    Point wanted = new(Math.Clamp(preferred[i].X, area.Left, area.Right - width),
                        Math.Clamp(preferred[i].Y, area.Top, area.Bottom - height));
                    var chosen = new Rect(wanted, new Size(width, height));
                    if (!Free(chosen))
                    {
                        var xs = occupied.SelectMany(r => new[] { r.Left, r.Right + horizontalSpacing, r.Left - width - horizontalSpacing })
                            .Append(wanted.X).Append(area.Left).Append(area.Right - width).Distinct();
                        var ys = occupied.SelectMany(r => new[] { r.Top, r.Bottom + verticalSpacing, r.Top - height - verticalSpacing })
                            .Append(wanted.Y).Append(area.Top).Append(area.Bottom - height).Distinct();
                        var candidates = (from x in xs from y in ys select new Rect(x, y, width, height))
                            .Where(Free).OrderBy(r => (r.TopLeft - wanted).LengthSquared).ToArray();
                        if (candidates.Length == 0) return false;
                        chosen = candidates[0];
                    }
                    bounds[i] = chosen;
                    occupied.Add(chosen);
                }
                return true;
            }
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
                    double remaining = area.Height - heights[c] - verticalSpacing - height;
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
                if (columns[column].Count > 0) heights[column] += verticalSpacing;
                columns[column].Add((i, width, height));
                heights[column] += height;
            }
            double totalWidth = columns.Sum(c => c.Max(item => item.Width)) + horizontalSpacing * (columns.Count - 1);
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
                    top += item.Height + verticalSpacing;
                }
                right = left - horizontalSpacing;
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
