using System.Windows;
using SysDash.SDK.Widgets;
using SysDash.Widgets.Common;

namespace SysDash.Widgets.Drive;

[Widget("drive", "Storage", Description = "Drive capacity and disk I/O rates",
    Category = WidgetCategories.Hardware)]
public sealed class DriveWidget : WidgetBase<DriveSettings, DriveViewModel>
{
    public override string Id => "drive";

    public override string DisplayName => "Storage";

    public override string Description => "Drive capacity usage and disk read/write rates";

    public override string Category => WidgetCategories.Hardware;

    protected override DriveViewModel CreateViewModel(IServiceProvider services, DriveSettings settings)
        => new(services.GetMarshaller(), settings);

    protected override FrameworkElement CreateView(DriveViewModel viewModel) => new DriveView();
}