using System.Windows;
using SysDash.SDK.Widgets;
using SysDash.Widgets.Common;

namespace SysDash.Widgets.Sensors;

[Widget("sensors", "Sensors", Description = "CPU / GPU temperatures, fans and every temperature sensor",
    Category = WidgetCategories.Hardware)]
public sealed class SensorsWidget : WidgetBase<SensorsSettings, SensorsViewModel>
{
    public override string Id => "sensors";

    public override string DisplayName => "Sensors";

    public override string Description => "CPU / GPU temperatures, fans and every temperature sensor";

    public override string Category => WidgetCategories.Hardware;

    protected override SensorsViewModel CreateViewModel(IServiceProvider services, SensorsSettings settings)
        => new(services.GetMarshaller(), settings);

    protected override FrameworkElement CreateView(SensorsViewModel viewModel) => new SensorsView();
}
