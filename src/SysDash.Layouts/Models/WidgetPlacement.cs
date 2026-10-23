namespace SysDash.Layouts.Models;

/// <summary>
/// Docked (sidebar) or detached (floating window) placement for one layout item.
/// DIP units, JSON-serializable with the shared camelCase settings.
/// </summary>
public sealed class WidgetPlacement
{
    /// <summary>True → hosted inside the sidebar shell; the window geometry fields are ignored.</summary>
    public bool Docked { get; set; } = true;

    /// <summary>
    /// True → the instance stays in the layout (its monitoring and settings stay
    /// alive) but is not materialized: no sidebar card and no floating window
    /// (P1 Hidden state). Persisted, so it survives restarts.
    /// </summary>
    public bool Hidden { get; set; }

    public double X { get; set; }

    public double Y { get; set; }

    public double Width { get; set; } = 320;

    public double Height { get; set; } = 200;

    /// <summary>Window opacity 0.05–1.</summary>
    public double Opacity { get; set; } = 1.0;

    /// <summary>Legacy flag kept for old layout files; <see cref="Layer"/> wins when set.</summary>
    public bool AlwaysOnTop { get; set; }

    /// <summary>Z-order layer: "top", "normal" or "bottom" (null → derived from <see cref="AlwaysOnTop"/>).</summary>
    public string? Layer { get; set; }

    /// <summary>
    /// True → the controller assigns a tidy on-screen position (and size) when the
    /// item is materialized, then clears the flag. Used by presets and new widgets.
    /// </summary>
    public bool AutoPlace { get; set; }

    public bool ClickThrough { get; set; }

    /// <summary>Snap floating windows to a grid while dragging.</summary>
    public bool SnapToGrid { get; set; } = true;

    /// <summary>Windows display number of the screen the detached window lives on (0 = unknown / main).</summary>
    public int MonitorIndex { get; set; }
    public string? MonitorDeviceId { get; set; }
    public double MonitorOffsetX { get; set; }
    public double MonitorOffsetY { get; set; }
    /// <summary>Content-height cap for a tidy initial layout; zero uses the screen height.</summary>
    public double HeightLimit { get; set; }

    /// <summary>Per-widget theme: "dark", "light", or null/empty = follow the app theme.</summary>
    public string? Theme { get; set; }
    public bool LargeSize { get; set; }
}
