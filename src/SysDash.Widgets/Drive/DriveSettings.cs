using SysDash.SDK.Widgets;

namespace SysDash.Widgets.Drive;

public sealed class DriveSettings : WidgetSettings
{
    [Setting("Drives", Group = "Source", Order = 0, OptionsSource = "drives",
        Description = "Show every drive, or just one.")]
    public string? PreferredDrive { get; set; }

    /// <summary>Legacy flag (kept for old layout files); an empty PreferredDrive means all drives.</summary>
    [Setting("All drives", Hidden = true)]
    public bool ShowAllDrives { get; set; } = true;

    [Setting("Drive list", Group = "Sections", Order = 10)]
    public bool ShowDrives { get; set; } = true;

    [Setting("Read / write activity", Group = "Sections", Order = 11)]
    public bool ShowActivity { get; set; } = true;

    [Setting("Low space warning below", Group = "Alerts", Order = 20, Min = 1, Max = 50, Step = 1, Unit = "% free",
        Description = "Rings turn red when a drive gets this full.")]
    public int LowSpacePercent { get; set; } = 10;

    [Setting("Graph time span", Group = "Graph", Order = 30, Min = 30, Max = 300, Step = 10, Unit = "s",
        Description = "How far back the graph goes (one bar per second at the default refresh rate).")]
    public int HistoryCapacity { get; set; } = 90;
}
