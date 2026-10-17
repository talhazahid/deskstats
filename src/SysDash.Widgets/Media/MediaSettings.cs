using SysDash.SDK.Widgets;

namespace SysDash.Widgets.Media;

public sealed class MediaSettings : WidgetSettings
{
    public bool ShowAppName { get; set; } = true;

    public bool ShowThumbnail { get; set; } = true;
}