using System.Text.Json.Serialization;

namespace SysDash.App;

/// <summary>
/// Global user preferences, persisted as JSON in %LocalAppData%\SysDash\settings.json.
/// </summary>
public sealed class AppSettings
{
    public string InstallationInstance { get; set; } = string.Empty;
    public bool AutomaticUpdates { get; set; } = true;
    public int WidgetPaddingLeft { get; set; } = 10;
    public int WidgetPaddingRight { get; set; } = 10;
    public int WidgetPaddingTop { get; set; } = 10;
    public int WidgetPaddingBottom { get; set; } = 10;
    /// <summary>Sensor polling interval in ms (200–60000).</summary>
    public int PollIntervalMs { get; set; } = 1000;

    /// <summary>"dark" or "light".</summary>
    public string Theme { get; set; } = "dark";

    /// <summary>Layout to activate on startup (matches a preset or saved layout name).</summary>
    public string ActiveLayout { get; set; } = "Default";

    public double SidebarWidth { get; set; } = 320;

    /// <summary>"right" or "left".</summary>
    public string SidebarEdge { get; set; } = "right";

    public bool SidebarAutoHide { get; set; } = true;

    public bool SidebarPinned { get; set; }

    /// <summary>Sidebar z-order: "top" (always on top), "normal", or "bottom" (behind all windows).</summary>
    public string SidebarLayer { get; set; } = "normal";

    public bool StartWithWindows { get; set; }

    /// <summary>The one-time "install sensor driver" tip was shown.</summary>
    public bool SensorDriverPromptShown { get; set; }

    /// <summary>The user made the first-launch sensor setup choice.</summary>
    public bool SensorDriverChoiceMade { get; set; }

    /// <summary>
    /// Screen the sidebar docks to: empty = main screen (automatic), otherwise the
    /// stable id of that physical monitor (MonitorDescriptor.DeviceId). Works the
    /// same on every PC — no screen numbers involved.
    /// </summary>
    public string SidebarScreenId { get; set; } = string.Empty;

    /// <summary>"widgets" (every widget floats on the desktop) or "sidebar" (all docked in the sidebar).</summary>
    public string DisplayMode { get; set; } = "widgets";

    /// <summary>Show the outer card frame (background + border) around widgets. False = sections only.</summary>
    /// Off by default (clean, frameless look). Stored under a new key so existing
    /// installs also start frameless; turn it back on in App settings or the widget menu.
    [JsonPropertyName("cardFrameV2")]
    public bool ShowCardFrame { get; set; }

    /// <summary>Show the sidebar's own background panel. False = transparent sidebar (restart needed).</summary>
    public bool SidebarBackground { get; set; } = true;

    /// <summary>Show the "DeskStats" title at the top of the sidebar.</summary>
    public bool SidebarShowTitle { get; set; } = true;

    /// <summary>Card background opacity for widgets docked in the sidebar (0–1; text stays solid).</summary>
    public double SidebarCardOpacity { get; set; } = 1.0;

    public bool LoadGpu { get; set; } = true;

    public bool LoadDrives { get; set; } = true;

    public bool LoadNetwork { get; set; } = true;
}
