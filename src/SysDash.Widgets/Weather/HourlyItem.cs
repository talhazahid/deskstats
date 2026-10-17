using SysDash.Core.Mvvm;

namespace SysDash.Widgets.Weather;

/// <summary>One hourly forecast entry.</summary>
public sealed class HourlyItem : ObservableObject
{
    public HourlyItem(string label, string temp, string icon, string precip = "")
    {
        Label = label;
        Temp = temp;
        Icon = icon;
        Precip = precip;
    }

    public string Label { get; }

    public string Temp { get; }

    public string Icon { get; }

    /// <summary>Chance of precipitation, e.g. "20%" (empty when unknown).</summary>
    public string Precip { get; }
}
