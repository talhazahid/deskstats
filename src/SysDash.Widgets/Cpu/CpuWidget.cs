using System.Windows;
using SysDash.SDK.Widgets;
using SysDash.Widgets.Common;

namespace SysDash.Widgets.Cpu;

[Widget("cpu", "CPU", Description = "Total and per-core CPU load, temperature, clock",
    Category = WidgetCategories.Hardware)]
public sealed class CpuWidget : WidgetBase<CpuSettings, CpuViewModel>
{
    public override string Id => "cpu";

    public override string DisplayName => "CPU";

    public override string Description => "Total and per-core CPU load, temperature, clock speed";

    public override string Category => WidgetCategories.Hardware;

    protected override CpuViewModel CreateViewModel(IServiceProvider services, CpuSettings settings)
        => new(services.GetMarshaller(), settings);

    protected override FrameworkElement CreateView(CpuViewModel viewModel) => new CpuView();
}