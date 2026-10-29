using System.Windows;

namespace SysDash.Theming.Native;

/// <summary>Monitor-relative placement avoids ambiguous global DIPs on mixed-DPI desktops.</summary>
public static class FloatingPlacement
{
    public const double Gap = 12;
    public const double Margin = 16;
    public static (Rect[] Bounds, double Scale) FitWithin(Rect work, IReadOnlyList<Size> sizes, IReadOnlyList<Point> preferred)
    {
        if (sizes.Count == 0) return (Array.Empty<Rect>(), 1);
        Rect area = work;
        area.Inflate(-Margin, -Margin);
        bool Pack(double scale, out Rect[] bounds)
        {
            bounds = new Rect[sizes.Count];
            var occupied = new List<Rect>();
            foreach (int i in Enumerable.Range(0, sizes.Count).OrderByDescending(i => sizes[i].Height * sizes[i].Width))
            {
                double width = Math.Ceiling(sizes[i].Width * scale);
                double height = Math.Ceiling(sizes[i].Height * scale);
                bool Free(Rect candidate) => area.Contains(candidate) && occupied.All(other =>
                {
                    other.Inflate(Gap + 1, Gap + 1);
                    return !other.IntersectsWith(candidate);
                });
                Rect chosen = new(preferred[i], new Size(width, height));
                if (!Free(chosen))
                {
                    var xs = occupied.SelectMany(r => new[] { r.Right + Gap + 2, r.Left - width - Gap - 2 })
                        .Append(area.Left).Append(area.Right - width).Distinct().OrderByDescending(x => x);
                    var ys = occupied.Select(r => r.Bottom + Gap + 2).Append(area.Top).Distinct().OrderBy(y => y);
                    Rect? found = null;
                    foreach (double y in ys)
                    {
                        foreach (double x in xs)
                        {
                            var candidate = new Rect(x, y, width, height);
                            if (!Free(candidate)) continue;
                            found = candidate;
                            break;
                        }
                        if (found.HasValue) break;
                    }
                    if (!found.HasValue) return false;
                    chosen = found.Value;
                }
                bounds[i] = chosen;
                occupied.Add(chosen);
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
