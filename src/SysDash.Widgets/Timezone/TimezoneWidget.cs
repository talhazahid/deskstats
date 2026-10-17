using System.Windows;
using SysDash.SDK.Widgets;
using SysDash.Widgets.Common;

namespace SysDash.Widgets.Timezone;

[Widget("timezone", "World Clock", Description = "Current time in several time zones",
    Category = WidgetCategories.Time)]
public sealed class TimezoneWidget : WidgetBase<TimezoneSettings, TimezoneViewModel>
{
    public override string Id => "timezone";

    public override string DisplayName => "World Clock";

    public override string Description => "Current time in several configurable time zones";

    public override string Category => WidgetCategories.Time;

    protected override TimezoneViewModel CreateViewModel(IServiceProvider services, TimezoneSettings settings)
        => new(services.GetMarshaller(), settings);

    protected override FrameworkElement CreateView(TimezoneViewModel viewModel) => new TimezoneView();
}