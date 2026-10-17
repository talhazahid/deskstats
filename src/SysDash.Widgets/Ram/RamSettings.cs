using SysDash.SDK.Widgets;

namespace SysDash.Widgets.Ram;

public sealed class RamSettings : WidgetSettings
{
    // New key (was ShowGraph): memory use barely moves, so the history graph is
    // off by default — existing widgets also start without it.
    [Setting("Usage graph", Group = "Sections", Order = 1, Description = "Memory use history under the rings.")]
    public bool ShowUsageGraph { get; set; }

    [Setting("Memory breakdown", Group = "Sections", Order = 2, Description = "In use, available, committed and installed memory.")]
    public bool ShowDetails { get; set; } = true;

    [Setting("Top apps", Group = "Sections", Order = 3, Description = "Apps using the most memory, refreshed every 3 seconds.")]
    public bool ShowProcesses { get; set; } = true;

    [Setting("Apps listed", Group = "Sections", Order = 4, Min = 1, Max = 30, Unit = "apps")]
    public int ProcessCount { get; set; } = 5;

    [Setting("Warning above", Group = "Alerts", Order = 20, Min = 50, Max = 100, Step = 1, Unit = "%",
        Description = "Shows a red badge when memory use passes this level.")]
    public int UsageAlertPercent { get; set; } = 90;

    [Setting("Graph time span", Group = "Graph", Order = 30, Min = 30, Max = 300, Step = 10, Unit = "s",
        Description = "How far back the graph goes (one bar per second at the default refresh rate).")]
    public int HistoryCapacity { get; set; } = 90;
}
