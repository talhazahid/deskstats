namespace SysDash.Layouts.Models;

/// <summary>One persisted widget instance inside a layout.</summary>
public sealed class LayoutItem
{
    /// <summary>Runtime identity; the app uses it to pair instances with windows.</summary>
    public Guid InstanceId { get; set; } = Guid.NewGuid();

    /// <summary>Widget type id, e.g. "cpu" (must match an IWidget registration).</summary>
    public string WidgetId { get; set; } = string.Empty;

    /// <summary>Optional display title override.</summary>
    public string? TitleOverride { get; set; }

    /// <summary>Serialized WidgetSettings for this instance (empty string → defaults).</summary>
    public string SettingsJson { get; set; } = string.Empty;

    public WidgetPlacement Placement { get; set; } = new();
}