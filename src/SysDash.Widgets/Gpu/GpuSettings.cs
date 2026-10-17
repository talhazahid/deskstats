using SysDash.SDK.Widgets;

namespace SysDash.Widgets.Gpu;

public sealed class GpuSettings : WidgetSettings
{
    [Setting("Graphics card", Group = "Source", Order = 0, OptionsSource = "gpus",
        Description = "Only shown when this PC has more than one GPU.")]
    public int GpuIndex { get; set; }

    [Setting("Load graph", Group = "Sections", Order = 1)]
    public bool ShowGraph { get; set; } = true;

    [Setting("Gauges", Group = "Sections", Order = 2, Description = "Load / VRAM / temperature / clock rings.")]
    public bool ShowRings { get; set; } = true;

    [Setting("Details list", Group = "Sections", Order = 3)]
    public bool ShowDetails { get; set; } = true;

    [Setting("Video memory", Group = "Values", Order = 10)]
    public bool ShowVram { get; set; } = true;

    [Setting("Temperature", Group = "Values", Order = 11)]
    public bool ShowTemperature { get; set; } = true;

    [Setting("Clock speed", Group = "Values", Order = 12)]
    public bool ShowClock { get; set; } = true;

    [Setting("Hot warning above", Group = "Alerts", Order = 20, Min = 60, Max = 100, Step = 1, Unit = "°C",
        Description = "Shows a red HOT badge when the GPU gets this warm.")]
    public int TempAlertC { get; set; } = 83;

    [Setting("Fahrenheit", Group = "Alerts", Order = 21)]
    public bool UseFahrenheit { get; set; }

    [Setting("Graph time span", Group = "Graph", Order = 30, Min = 30, Max = 300, Step = 10, Unit = "s",
        Description = "How far back the graph goes (one bar per second at the default refresh rate).")]
    public int HistoryCapacity { get; set; } = 90;
}
