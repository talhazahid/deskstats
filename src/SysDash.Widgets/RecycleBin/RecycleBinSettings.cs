using SysDash.SDK.Widgets;

namespace SysDash.Widgets.RecycleBin;

public sealed class RecycleBinSettings : WidgetSettings
{
    public bool ShowSize { get; set; } = true;

    /// <summary>Seconds between refreshes. Clamped 2–300.</summary>
    public int RefreshSeconds { get; set; } = 5;
}