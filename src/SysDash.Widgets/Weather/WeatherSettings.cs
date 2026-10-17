using SysDash.SDK.Widgets;

namespace SysDash.Widgets.Weather;

public sealed class WeatherSettings : WidgetSettings
{
    [Setting("City", Group = "Location", Order = 1, Lookup = "city",
        Description = "Start typing (e.g. \"kar\") and pick your city from the list.")]
    public string LocationName { get; set; } = "New York";

    /// <summary>City the coordinates below were looked up for (internal).</summary>
    [Setting("Resolved location", Hidden = true)]
    public string? ResolvedLocation { get; set; } = "New York";

    [Setting("Latitude", Group = "Advanced", Order = 50, Description = "Filled in automatically from the city.")]
    public double Latitude { get; set; } = 40.7128;

    [Setting("Longitude", Group = "Advanced", Order = 51)]
    public double Longitude { get; set; } = -74.0060;

    [Setting("Hourly forecast", Group = "Display", Order = 10)]
    public bool ShowHourly { get; set; } = true;

    [Setting("Daily forecast", Group = "Display", Order = 11, Description = "Next days with high / low temperature.")]
    public bool ShowDaily { get; set; } = true;

    /// <summary>How many days after today to show (1–6).</summary>
    [Setting("Days to show", Group = "Display", Order = 12, Min = 1, Max = 6, Step = 1, Unit = "days")]
    public int DailyDays { get; set; } = 4;

    [Setting("Fahrenheit", Group = "Display", Order = 13)]
    public bool UseFahrenheit { get; set; }

    /// <summary>Minutes between refreshes. Clamped 10–180.</summary>
    [Setting("Refresh every", Group = "Display", Order = 14, Min = 10, Max = 180, Step = 5, Unit = "min")]
    public int RefreshMinutes { get; set; } = 30;
}
