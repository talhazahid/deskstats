using System.Windows;
using SysDash.SDK.Widgets;
using SysDash.Widgets.Common;

namespace SysDash.Widgets.Power;

[Widget("power", "Hardware power", Description = "CPU, GPU and supported PSU power readings with explicit measurement scopes", Category = WidgetCategories.Hardware)]
public sealed class PowerWidget : WidgetBase<PowerSettings, PowerViewModel>
{
    public override string Id => "power";
    public override string DisplayName => "Hardware power";
    public override string Description => "CPU, GPU and supported PSU power readings";
    public override string Category => WidgetCategories.Hardware;
    protected override PowerViewModel CreateViewModel(IServiceProvider services, PowerSettings settings) => new(services.GetMarshaller(), settings);
    protected override FrameworkElement CreateView(PowerViewModel viewModel) => new PowerView();
}

public sealed class PowerSettings : WidgetSettings { }
