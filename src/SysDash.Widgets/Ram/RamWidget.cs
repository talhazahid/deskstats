using System.Windows;
using SysDash.SDK.Widgets;
using SysDash.Widgets.Common;

namespace SysDash.Widgets.Ram;

[Widget("ram", "Memory", Description = "RAM usage percent, used/total and history",
    Category = WidgetCategories.Hardware)]
public sealed class RamWidget : WidgetBase<RamSettings, RamViewModel>
{
    public override string Id => "ram";

    public override string DisplayName => "Memory";

    public override string Description => "RAM usage percent, used/total amount and history";

    public override string Category => WidgetCategories.Hardware;

    protected override RamViewModel CreateViewModel(IServiceProvider services, RamSettings settings)
        => new(services.GetMarshaller(), settings);

    protected override FrameworkElement CreateView(RamViewModel viewModel) => new RamView();
}