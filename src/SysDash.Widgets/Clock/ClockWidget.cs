using System.Windows;
using SysDash.SDK.Widgets;
using SysDash.Widgets.Common;

namespace SysDash.Widgets.Clock;

[Widget("clock", "Date & Time", Description = "Clock with date and a month calendar",
    Category = WidgetCategories.Time)]
public sealed class ClockWidget : WidgetBase<ClockSettings, ClockViewModel>
{
    public override string Id => "clock";

    public override string DisplayName => "Date & Time";

    public override string Description => "Clock with date and a month calendar";

    public override string Category => WidgetCategories.Time;

    protected override ClockViewModel CreateViewModel(IServiceProvider services, ClockSettings settings)
        => new(services.GetMarshaller(), settings);

    protected override FrameworkElement CreateView(ClockViewModel viewModel) => new ClockView();
}