using System.Windows;
using SysDash.SDK.Widgets;
using SysDash.Widgets.Common;

namespace SysDash.Widgets.Network;

[Widget("network", "Network", Description = "Download/upload rates with adapters and IP",
    Category = WidgetCategories.Hardware)]
public sealed class NetworkWidget : WidgetBase<NetworkSettings, NetworkViewModel>
{
    public override string Id => "network";

    public override string DisplayName => "Network";

    public override string Description => "Download and upload rates, adapter and IP address";

    public override string Category => WidgetCategories.Hardware;

    protected override NetworkViewModel CreateViewModel(IServiceProvider services, NetworkSettings settings)
        => new(services.GetMarshaller(), settings);

    protected override FrameworkElement CreateView(NetworkViewModel viewModel) => new NetworkView();
}