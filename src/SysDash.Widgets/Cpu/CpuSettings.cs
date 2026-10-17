using SysDash.SDK.Widgets;

namespace SysDash.Widgets.Cpu;

public sealed class CpuSettings : WidgetSettings
{
    [Setting("Load graph", Group = "Sections", Order = 1)]
    public bool ShowGraph { get; set; } = true;

    [Setting("Per-core rings", Group = "Sections", Order = 2, Description = "One ring per logical processor (thread).")]
    public bool ShowPerCore { get; set; } = true;

    [Setting("Cores shown", Group = "Sections", Order = 3, Min = 1, Max = 256, Unit = "cores", MaxSource = "cpuThreads")]
    public int MaxCoresShown { get; set; } = 256;

    [Setting("Details list", Group = "Sections", Order = 4, Description = "Temperature, clock, power, voltage and fan.")]
    public bool ShowDetails { get; set; } = true;

    [Setting("Clock speed in header", Group = "Header", Order = 10)]
    public bool ShowClock { get; set; } = true;

    [Setting("Temperature in header", Group = "Header", Order = 11)]
    public bool ShowTemperature { get; set; } = true;

    [Setting("Hot warning above", Group = "Alerts", Order = 20, Min = 60, Max = 100, Step = 1, Unit = "°C",
        Description = "Shows a red HOT badge when the CPU gets this warm.")]
    public int TempAlertC { get; set; } = 85;

    [Setting("Fahrenheit", Group = "Alerts", Order = 21)]
    public bool UseFahrenheit { get; set; }

    [Setting("Graph time span", Group = "Graph", Order = 30, Min = 30, Max = 300, Step = 10, Unit = "s",
        Description = "How far back the graph goes (one bar per second at the default refresh rate).")]
    public int HistoryCapacity { get; set; } = 90;
}
