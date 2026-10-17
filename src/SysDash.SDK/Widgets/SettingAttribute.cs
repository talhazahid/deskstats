namespace SysDash.SDK.Widgets;

/// <summary>
/// Describes how a widget setting is shown in the settings window: label,
/// help text, group, order and — for numbers — a slider range. Properties
/// without this attribute still appear (humanized name, plain input).
/// </summary>
[AttributeUsage(AttributeTargets.Property, Inherited = true)]
public sealed class SettingAttribute : Attribute
{
    public SettingAttribute(string label)
    {
        Label = label;
    }

    public string Label { get; }

    /// <summary>One-line help shown under the label.</summary>
    public string? Description { get; set; }

    /// <summary>Section in the settings window ("Display", "Data", …).</summary>
    public string Group { get; set; } = "General";

    public int Order { get; set; }

    /// <summary>Slider minimum. Leave Min = Max (default) for a plain number box.</summary>
    public double Min { get; set; }

    public double Max { get; set; }

    public double Step { get; set; } = 1;

    /// <summary>Unit shown next to the slider value ("s", "%", "rows").</summary>
    public string? Unit { get; set; }

    /// <summary>
    /// Clamp the slider maximum to something detected at runtime:
    /// "cpuThreads" = logical processors reported by the sensors.
    /// </summary>
    public string? MaxSource { get; set; }

    /// <summary>
    /// Show a dropdown of things detected on this PC instead of a number/text box:
    /// "gpus" (int index), "adapters" (string name, empty = automatic),
    /// "drives" (string name, empty = all drives). A "gpus" row is hidden when
    /// the PC has only one GPU.
    /// </summary>
    public string? OptionsSource { get; set; }

    /// <summary>
    /// Text box with online suggestions: "city" = type-ahead city search
    /// (Open-Meteo geocoding). Picking a city also fills sibling properties
    /// named Latitude, Longitude and ResolvedLocation when they exist.
    /// </summary>
    public string? Lookup { get; set; }

    /// <summary>Hide from the settings window.</summary>
    public bool Hidden { get; set; }
}
