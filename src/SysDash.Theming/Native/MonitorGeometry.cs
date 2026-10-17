using System.Windows;

namespace SysDash.Theming.Native;

/// <summary>
/// One physical monitor, with bounds/work area converted to DIPs.
/// <see cref="Number"/> is the Windows display number ("\\.\DISPLAY3" → 3), the
/// same number Windows Settings → Display shows and "Identify" flashes on screen.
/// It is stable across restarts, unlike the enumeration order (<see cref="Index"/>).
/// </summary>
public sealed record MonitorDescriptor(
    int Index,
    string DeviceName,
    Rect BoundsDip,
    Rect WorkAreaDip,
    int DpiX,
    int DpiY,
    bool IsPrimary)
{
    public int Number { get; init; } = Index + 1;

    /// <summary>Monitor model from its EDID (e.g. "DELL P2419H"), or empty when unknown.</summary>
    public string FriendlyName { get; init; } = string.Empty;

    /// <summary>
    /// Stable identity of this physical screen (monitor device path, which survives
    /// restarts and re-plugging into the same port); falls back to the GDI name.
    /// </summary>
    public string DeviceId { get; init; } = DeviceName;

    public int PixelWidth => (int)Math.Round(BoundsDip.Width * DpiX / 96.0);

    public int PixelHeight => (int)Math.Round(BoundsDip.Height * DpiY / 96.0);
}

/// <summary>
/// Monitor enumeration + work-area geometry. Windows coordinates are physical
/// pixels; WPF window placement is in DIPs, so everything here converts with
/// the per-monitor DPI (physical / (dpi/96)).
/// </summary>
public static class MonitorGeometry
{
    /// <summary>Enumerates all attached monitors, primary not guaranteed first.</summary>
    public static IReadOnlyList<MonitorDescriptor> EnumerateMonitors()
    {
        var list = new List<MonitorDescriptor>();
        IReadOnlyDictionary<string, (string Name, string Path)> names = DisplayNames.ByGdiDeviceName();

        bool Callback(IntPtr hMonitor, IntPtr hdc, IntPtr lprc, IntPtr dwData)
        {
            try
            {
                var info = new NativeMethods.MONITORINFOEXW
                {
                    cbSize = (uint)System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.MONITORINFOEXW>(),
                    szDevice = string.Empty,
                };

                if (NativeMethods.GetMonitorInfoW(hMonitor, ref info))
                {
                    int dpiX = 96;
                    int dpiY = 96;
                    if (NativeMethods.GetDpiForMonitor(hMonitor, 0, out int x, out int y) == 0 && x > 0 && y > 0)
                    {
                        dpiX = x;
                        dpiY = y;
                    }

                    int index = list.Count;
                    list.Add(new MonitorDescriptor(
                        index,
                        info.szDevice,
                        ToDip(info.rcMonitor, dpiX, dpiY),
                        ToDip(info.rcWork, dpiX, dpiY),
                        dpiX,
                        dpiY,
                        (info.dwFlags & NativeMethods.MonitorInfoFPrimary) != 0)
                    {
                        Number = DisplayNumber(info.szDevice) ?? index + 1,
                        FriendlyName = names.TryGetValue(info.szDevice, out var n1) ? n1.Name : string.Empty,
                        DeviceId = names.TryGetValue(info.szDevice, out var n2) && n2.Path.Length > 0 ? n2.Path : info.szDevice,
                    });
                }
            }
            catch
            {
                // Skip a broken monitor entry but keep enumerating.
            }

            return true;
        }

        NativeMethods.EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, Callback, IntPtr.Zero);
        return list;
    }

    /// <summary>Work area of the primary monitor in DIPs (or virtual screen when no monitors are found).</summary>
    public static Rect PrimaryWorkAreaDip()
    {
        foreach (MonitorDescriptor m in EnumerateMonitors())
        {
            if (m.IsPrimary)
            {
                return m.WorkAreaDip;
            }
        }

        return new Rect(0, 0, 1280, 800);
    }

    /// <summary>Monitor whose bounds contain the point (DIPs), or null.</summary>
    public static MonitorDescriptor? MonitorAt(Point point)
    {
        foreach (MonitorDescriptor m in EnumerateMonitors())
        {
            if (m.BoundsDip.Contains(point))
            {
                return m;
            }
        }

        return null;
    }

    /// <summary>
    /// Work area for a saved widget position: the monitor that contains the point,
    /// else the saved screen (Windows display number) if still connected, else the
    /// main screen — so windows follow you across screens and come back on-screen
    /// when a monitor is unplugged.
    /// </summary>
    public static Rect WorkAreaFor(Point point, int screenNumber)
    {
        IReadOnlyList<MonitorDescriptor> monitors = EnumerateMonitors();
        foreach (MonitorDescriptor m in monitors)
        {
            if (m.BoundsDip.Contains(point))
            {
                return m.WorkAreaDip;
            }
        }

        return FindByNumber(monitors, screenNumber)?.WorkAreaDip ?? PrimaryWorkAreaDip();
    }

    /// <summary>Work area of the screen with this Windows display number; 0 / missing = main screen.</summary>
    public static Rect WorkAreaOf(int screenNumber)
        => FindByNumber(EnumerateMonitors(), screenNumber)?.WorkAreaDip ?? PrimaryWorkAreaDip();

    /// <summary>Monitor with this stable id (see <see cref="MonitorDescriptor.DeviceId"/>), or null.</summary>
    public static MonitorDescriptor? FindById(string? deviceId)
        => string.IsNullOrEmpty(deviceId)
            ? null
            : EnumerateMonitors().FirstOrDefault(m => string.Equals(m.DeviceId, deviceId, StringComparison.OrdinalIgnoreCase));

    /// <summary>Work area of the screen with this id; empty / unplugged = main screen.</summary>
    public static Rect WorkAreaOfId(string? deviceId) => FindById(deviceId)?.WorkAreaDip ?? PrimaryWorkAreaDip();

    /// <summary>True when the id means the main screen (empty = automatic, unplugged, or the primary itself).</summary>
    public static bool IsMainScreen(string? deviceId)
    {
        MonitorDescriptor? m = FindById(deviceId);
        return m is null || m.IsPrimary;
    }

    /// <summary>
    /// Every screen with a plain-language label that needs no numbers, for ANY
    /// setup: where it sits ("Left", "Middle", "Right", plus "top"/"bottom" when
    /// stacked), the model from its EDID, its resolution, and "main".
    /// e.g. "Left · DELL P2419H · 1920 × 1080 · main".
    /// (Windows offers no API for the numbers its Settings page shows — they vary
    /// with the GPU driver and ports — so DeskStats deliberately does not use them.)
    /// </summary>
    public static IReadOnlyList<(MonitorDescriptor Monitor, string Label)> DescribeMonitors()
    {
        IReadOnlyList<MonitorDescriptor> monitors = EnumerateMonitors();
        var labels = new List<(MonitorDescriptor, string)>();

        foreach (MonitorDescriptor m in monitors.OrderBy(x => x.BoundsDip.Left).ThenBy(x => x.BoundsDip.Top))
        {
            var parts = new List<string>();
            string place = PlaceOf(m, monitors);
            if (place.Length > 0)
            {
                parts.Add(place);
            }

            if (!string.IsNullOrWhiteSpace(m.FriendlyName))
            {
                parts.Add(m.FriendlyName);
            }

            parts.Add($"{m.PixelWidth} × {m.PixelHeight}");
            if (m.IsPrimary)
            {
                parts.Add("main");
            }

            labels.Add((m, string.Join(" · ", parts)));
        }

        // Two identical screens in the same spot description: add a counter.
        foreach (var group in labels.GroupBy(l => l.Item2).Where(g => g.Count() > 1).ToList())
        {
            int k = 1;
            foreach (var item in group.ToList())
            {
                int i = labels.IndexOf(item);
                labels[i] = (item.Item1, $"{item.Item2} ({k++})");
            }
        }

        return labels;
    }

    /// <summary>"Left" / "Middle" / "Right" (columns by x), plus "top"/"bottom" for stacked screens.</summary>
    private static string PlaceOf(MonitorDescriptor m, IReadOnlyList<MonitorDescriptor> all)
    {
        if (all.Count < 2)
        {
            return string.Empty;
        }

        static bool SameColumn(Rect a, Rect b)
        {
            double overlap = Math.Min(a.Right, b.Right) - Math.Max(a.Left, b.Left);
            return overlap > Math.Min(a.Width, b.Width) * 0.5;
        }

        // Columns: group screens that sit above each other.
        var columns = new List<List<MonitorDescriptor>>();
        foreach (MonitorDescriptor screen in all.OrderBy(x => x.BoundsDip.Left))
        {
            List<MonitorDescriptor>? col = columns.FirstOrDefault(c => c.Any(o => SameColumn(o.BoundsDip, screen.BoundsDip)));
            if (col is null)
            {
                columns.Add(new List<MonitorDescriptor> { screen });
            }
            else
            {
                col.Add(screen);
            }
        }

        int ci = columns.FindIndex(c => c.Contains(m));
        string horizontal = columns.Count switch
        {
            1 => string.Empty,
            _ when ci == 0 => "Left",
            _ when ci == columns.Count - 1 => "Right",
            3 => "Middle",
            _ => $"Middle {ci}",
        };

        string vertical = string.Empty;
        List<MonitorDescriptor> column = columns[ci];
        if (column.Count > 1)
        {
            var byY = column.OrderBy(x => x.BoundsDip.Top).ToList();
            int yi = byY.IndexOf(m);
            vertical = yi == 0 ? "top" : yi == byY.Count - 1 ? "bottom" : "centre";
        }

        string text = $"{horizontal} {vertical}".Trim();
        return text.Length > 0 ? char.ToUpperInvariant(text[0]) + text[1..] : text;
    }

    private static MonitorDescriptor? FindByNumber(IReadOnlyList<MonitorDescriptor> monitors, int number)
        => number > 0 ? monitors.FirstOrDefault(m => m.Number == number) : null;

    /// <summary>"\\.\DISPLAY3" → 3.</summary>
    private static int? DisplayNumber(string deviceName)
    {
        if (string.IsNullOrEmpty(deviceName))
        {
            return null;
        }

        int i = deviceName.Length;
        while (i > 0 && char.IsDigit(deviceName[i - 1]))
        {
            i--;
        }

        return i < deviceName.Length && int.TryParse(deviceName.AsSpan(i), out int n) && n > 0 ? n : null;
    }

    /// <summary>Aim the given rect inside the work area, clamped so it stays reachable.</summary>
    public static Rect ClampToWorkArea(Rect rect, Rect workArea)
    {
        double x = Math.Clamp(rect.X, workArea.Left, Math.Max(workArea.Left, workArea.Right - rect.Width));
        double y = Math.Clamp(rect.Y, workArea.Top, Math.Max(workArea.Top, workArea.Bottom - rect.Height));
        double width = Math.Min(rect.Width, workArea.Width);
        double height = Math.Min(rect.Height, workArea.Height);
        return new Rect(x, y, width, height);
    }

    private static Rect ToDip(NativeMethods.RECT r, int dpiX, int dpiY)
    {
        double sx = 96.0 / dpiX;
        double sy = 96.0 / dpiY;
        return new Rect(r.Left * sx, r.Top * sy, (r.Right - r.Left) * sx, (r.Bottom - r.Top) * sy);
    }
}