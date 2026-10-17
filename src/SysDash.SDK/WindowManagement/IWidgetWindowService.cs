using SysDash.SDK.Hosting;
using SysDash.SDK.Widgets;

namespace SysDash.SDK.WindowManagement;

/// <summary>
/// Owns detached widget windows. Widgets never manipulate System.Windows.Window
/// directly — placement, pinning, click-through and opacity all go through here.
/// </summary>
public interface IWidgetWindowService
{
    Guid CreateWindow(WidgetInstance instance, WidgetPlacementSpec placement);

    void Close(Guid windowId);

    bool TryGetHost(Guid windowId, out IWidgetHost host);

    void BringToFront(Guid windowId);

    void SetAlwaysOnTop(Guid windowId, bool alwaysOnTop);

    /// <summary>Sets the z-order layer: "top", "normal" or "bottom" (behind all windows).</summary>
    void SetLayer(Guid windowId, string layer);

    void SetClickThrough(Guid windowId, bool clickThrough);

    void SetOpacity(Guid windowId, double opacity);

    /// <summary>Per-window theme: "dark", "light", or null = follow the app.</summary>
    void SetTheme(Guid windowId, string? theme);

    /// <summary>Moves the window to the given DIP position (no persistence).</summary>
    void MoveTo(Guid windowId, double x, double y);
}

/// <summary>Placement data handed to the window service at creation time.</summary>
public sealed class WidgetPlacementSpec
{
    public double X { get; set; }

    public double Y { get; set; }

    public double Width { get; set; } = 280;

    public double Height { get; set; } = 160;

    public double Opacity { get; set; } = 1.0;

    public bool AlwaysOnTop { get; set; }

    /// <summary>"top", "normal" or "bottom". Null falls back to <see cref="AlwaysOnTop"/>.</summary>
    public string? Layer { get; set; }

    public bool ClickThrough { get; set; }

    public bool SnapToGrid { get; set; } = true;

    public int MonitorIndex { get; set; }

    /// <summary>"dark", "light", or null = follow the app theme.</summary>
    public string? Theme { get; set; }
}