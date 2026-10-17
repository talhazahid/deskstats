using SysDash.SDK.Widgets;

namespace SysDash.Widgets.Sensors;

public sealed class SensorsSettings : WidgetSettings
{
    [Setting("Temperature list", Group = "Sections", Order = 1)]
    public bool ShowTemperatures { get; set; } = true;

    [Setting("Sensors listed", Group = "Sections", Order = 2, Min = 4, Max = 30, Unit = "rows",
        Description = "The hottest sensors are listed first.")]
    public int MaxTemperatureRows { get; set; } = 12;

    [Setting("Fans", Group = "Sections", Order = 3)]
    public bool ShowFans { get; set; } = true;

    [Setting("Max fan speed", Group = "Sections", Order = 4, Min = 500, Max = 5000, Step = 100, Unit = "RPM",
        Description = "Fan rings are full at this speed.")]
    public int FanFullScaleRpm { get; set; } = 2500;

    [Setting("Hot warning above", Group = "Alerts", Order = 10, Min = 60, Max = 100, Step = 1, Unit = "°C")]
    public int TempAlertC { get; set; } = 85;

    [Setting("Fahrenheit", Group = "Alerts", Order = 11)]
    public bool UseFahrenheit { get; set; }
}
