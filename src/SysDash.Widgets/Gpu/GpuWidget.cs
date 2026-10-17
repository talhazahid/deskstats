using System.Windows;
using SysDash.SDK.Widgets;
using SysDash.Widgets.Common;

namespace SysDash.Widgets.Gpu;

[Widget("gpu", "GPU", Description = "GPU load, VRAM, temperature and clock",
    Category = WidgetCategories.Hardware)]
public sealed class GpuWidget : WidgetBase<GpuSettings, GpuViewModel>
{
    public override string Id => "gpu";

    public override string DisplayName => "GPU";

    public override string Description => "GPU load, VRAM usage, temperature and clock speed";

    public override string Category => WidgetCategories.Hardware;

    protected override GpuViewModel CreateViewModel(IServiceProvider services, GpuSettings settings)
        => new(services.GetMarshaller(), settings);

    protected override FrameworkElement CreateView(GpuViewModel viewModel) => new GpuView();
}