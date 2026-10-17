using SysDash.SDK.Widgets;

namespace SysDash.Widgets.Network;

public sealed class NetworkSettings : WidgetSettings
{
    [Setting("Network adapter", Group = "Source", Order = 0, OptionsSource = "adapters",
        Description = "Automatic follows the adapter with the most traffic.")]
    public string? AdapterName { get; set; }

    /// <summary>Legacy flag (kept for old layout files); an empty AdapterName means automatic.</summary>
    [Setting("Follow busiest adapter", Hidden = true)]
    public bool UseFastestAdapter { get; set; } = true;

    [Setting("Speed in bits (Mbps)", Group = "Units", Order = 5,
        Description = "Matches how internet plans and speed tests are measured. Off = bytes (MB/s).")]
    public bool UseBits { get; set; }

    [Setting("Traffic graph", Group = "Sections", Order = 10)]
    public bool ShowGraph { get; set; } = true;

    [Setting("Public IP address", Group = "Sections", Order = 11,
        Description = "Looked up at api.ipify.org every 30 minutes.")]
    public bool ShowPublicIp { get; set; } = true;

    [Setting("Local IP address", Group = "Sections", Order = 12)]
    public bool ShowIp { get; set; } = true;

    [Setting("Data used this session", Group = "Sections", Order = 13)]
    public bool ShowTotals { get; set; } = true;

    [Setting("Graph time span", Group = "Graph", Order = 30, Min = 30, Max = 300, Step = 10, Unit = "s",
        Description = "How far back the graph goes (one bar per second at the default refresh rate).")]
    public int HistoryCapacity { get; set; } = 90;
}
