using SysDash.SDK.Widgets;

namespace SysDash.Widgets.Timezone;

public sealed class TimezoneSettings : WidgetSettings
{
    /// <summary>Windows time zone ids to display, in order. Empty resets to the defaults.</summary>
    public List<string> TimeZoneIds { get; set; } = new()
    {
        "UTC",
        "Eastern Standard Time",
        "Central European Standard Time",
        "Tokyo Standard Time",
        "Pacific Standard Time",
    };

    public bool ShowSeconds { get; set; }
}