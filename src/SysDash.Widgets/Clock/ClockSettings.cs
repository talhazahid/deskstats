using SysDash.SDK.Widgets;

namespace SysDash.Widgets.Clock;

public sealed class ClockSettings : WidgetSettings
{
    [Setting("Seconds", Group = "Clock", Order = 1)]
    public bool ShowSeconds { get; set; } = true;

    [Setting("24-hour time", Group = "Clock", Order = 2)]
    public bool Use24Hour { get; set; }

    [Setting("Date", Group = "Clock", Order = 3)]
    public bool ShowDate { get; set; } = true;

    [Setting("Month calendar", Group = "Calendar", Order = 10)]
    public bool ShowCalendar { get; set; } = true;

    [Setting("Week starts Monday", Group = "Calendar", Order = 11)]
    public bool WeekStartsMonday { get; set; } = true;

    /// <summary>Full DateTime format override; takes precedence over ShowSeconds/Use24Hour.</summary>
    [Setting("Custom time format", Group = "Advanced", Order = 20, Description = ".NET format string, e.g. HH:mm:ss. Leave empty for automatic.")]
    public string? FormatOverride { get; set; }
}
