using System.Windows;
using SysDash.SDK.Widgets;
using SysDash.Widgets.Common;

namespace SysDash.Widgets.Media;

[Widget("media", "Now Playing", Description = "Currently playing media title, artist and album art",
    Category = WidgetCategories.System)]
public sealed class MediaWidget : WidgetBase<MediaSettings, MediaViewModel>
{
    public override string Id => "media";

    public override string DisplayName => "Now Playing";

    public override string Description => "Currently playing media title, artist and album art via Windows Media Control";

    public override string Category => WidgetCategories.System;

    protected override MediaViewModel CreateViewModel(IServiceProvider services, MediaSettings settings)
        => new(services.GetMarshaller(), settings);

    protected override FrameworkElement CreateView(MediaViewModel viewModel) => new MediaView();
}