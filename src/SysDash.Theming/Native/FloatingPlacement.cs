using System.Windows;

namespace SysDash.Theming.Native;

/// <summary>Monitor-relative placement avoids ambiguous global DIPs on mixed-DPI desktops.</summary>
public static class FloatingPlacement
{
    public const double Gap = 12;
    public const double Margin = 16;
    public static Rect PixelWorkArea(MonitorDescriptor monitor)
        => new(monitor.WorkAreaDip.X * monitor.DpiX / 96.0, monitor.WorkAreaDip.Y * monitor.DpiY / 96.0,
            monitor.WorkAreaDip.Width * monitor.DpiX / 96.0, monitor.WorkAreaDip.Height * monitor.DpiY / 96.0);

    public static Point RestorePixels(MonitorDescriptor monitor, double offsetX, double offsetY, double width, double height)
    {
        Rect work = monitor.WorkAreaDip;
        // Keep intentional edge placement; only rescue positions with no reachable content.
        double x = Math.Clamp(offsetX, -Math.Max(0, width - 48), Math.Max(0, work.Width - 48));
        double y = Math.Clamp(offsetY, -Math.Max(0, height - 48), Math.Max(0, work.Height - 48));
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
