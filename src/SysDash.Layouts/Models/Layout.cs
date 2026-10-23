namespace SysDash.Layouts.Models;

/// <summary>
/// A named collection of widget instances plus an optional default theme.
/// Plain POCO — export/import is copy/paste of one JSON file.
/// </summary>
public sealed class Layout
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    /// <summary>Unique display name; also the storage key when saved.</summary>
    public string Name { get; set; } = "Layout";

    /// <summary>Themes: "dark" / "light". Null keeps the current theme when applied.</summary>
    public string? Theme { get; set; }
    public bool HasSavedState { get; set; }

    public List<LayoutItem> Items { get; set; } = new();

    public Layout Clone()
        => new()
        {
            Id = Id,
            Name = Name,
            Theme = Theme,
            HasSavedState = HasSavedState,
            Items = Items.Select(i => new LayoutItem
            {
                InstanceId = i.InstanceId,
                WidgetId = i.WidgetId,
                TitleOverride = i.TitleOverride,
                SettingsJson = i.SettingsJson,
                Placement = new WidgetPlacement
                {
                    Docked = i.Placement.Docked,
                    Hidden = i.Placement.Hidden,
                    X = i.Placement.X,
                    Y = i.Placement.Y,
                    Width = i.Placement.Width,
                    Height = i.Placement.Height,
                    Opacity = i.Placement.Opacity,
                    AlwaysOnTop = i.Placement.AlwaysOnTop,
                    Layer = i.Placement.Layer,
                    AutoPlace = i.Placement.AutoPlace,
                    ClickThrough = i.Placement.ClickThrough,
                    SnapToGrid = i.Placement.SnapToGrid,
                    MonitorIndex = i.Placement.MonitorIndex,
                    MonitorDeviceId = i.Placement.MonitorDeviceId,
                    MonitorOffsetX = i.Placement.MonitorOffsetX,
                    MonitorOffsetY = i.Placement.MonitorOffsetY,
                    HeightLimit = i.Placement.HeightLimit,
                    Theme = i.Placement.Theme,
                    LargeSize = i.Placement.LargeSize,
                },
            }).ToList(),
        };
}
