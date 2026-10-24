using SysDash.Layouts.Models;

namespace SysDash.Layouts;

/// <summary>
/// Seed layouts shipped with the product. They are templates: when a layout
/// name is activated and no saved file exists yet, the preset is used as a
/// starting point. Saving the active layout attaches a file to that name.
/// </summary>
public static class LayoutPresets
{
    public const string Default = "Default";
    public const string Minimal = "Minimal";
    public const string Gaming = "Gaming";
    public const string Work = "Work";
    public const string Streaming = "Streaming";

    public static IReadOnlyList<string> Names { get; } =
        new[] { Default, Minimal, Gaming, Work, Streaming };

    public static bool IsPreset(string name)
        => Names.Contains(name, StringComparer.OrdinalIgnoreCase);

    public static Layout? TryGet(string name)
    {
        string? key = Names.FirstOrDefault(n => n.Equals(name, StringComparison.OrdinalIgnoreCase));
        return key is null ? null : Build(key);
    }

    public static Layout Build(string name) => name switch
    {
        Minimal => Layout(Minimal, "dark",
            Item("clock"),
            Item("cpu"),
            Item("ram"),
            Item("network")),
        Gaming => Layout(Gaming, "dark",
            Item("cpu"),
            Item("gpu"),
            Item("sensors"),
            Item("ram"),
            Item("network"),
            Item("drive")),
        Work => Layout(Work, "light",
            Item("clock"),
            Item("cpu"),
            Item("ram"),
            Item("drive"),
            Item("network"),
            Item("timezone")),
        Streaming => Layout(Streaming, "dark",
            Item("clock"),
            Item("cpu"),
            Item("gpu"),
            Item("ram"),
            Item("network"),
            Item("drive"),
            Item("media")),
        _ => Layout(Default, "dark",
            Item("cpu"),
            Item("gpu"),
            Item("ram"),
            Item("network"),
            Item("drive"),
            Item("sensors"),
            Item("power")),
    };

    private static Layout Layout(string name, string? theme, params LayoutItem[] items)
        => new()
        {
            Id = name.ToLowerInvariant(),
            Name = name,
            Theme = theme,
            Items = items.ToList(),
        };

    private static LayoutItem Item(string widgetId, string? title = null)
        => new()
        {
            WidgetId = widgetId,
            TitleOverride = title,
            SettingsJson = string.Empty,
            // Rainmeter-style: presets launch as floating widgets (no sidebar);
            // the app lays them out on first show (AutoPlace).
            Placement = new WidgetPlacement { Docked = false, AutoPlace = true, Layer = "normal" },
        };
}