using System.Windows;
using SysDash.SDK.Widgets;
using SysDash.Widgets.Common;

namespace SysDash.Widgets.RecycleBin;

[Widget("recyclebin", "Recycle Bin", Description = "Item count and size with an empty-bin action",
    Category = WidgetCategories.System)]
public sealed class RecycleBinWidget : WidgetBase<RecycleBinSettings, RecycleBinViewModel>
{
    public override string Id => "recyclebin";

    public override string DisplayName => "Recycle Bin";

    public override string Description => "Recycle bin item count and size with a one-click empty action";

    public override string Category => WidgetCategories.System;

    protected override RecycleBinViewModel CreateViewModel(IServiceProvider services, RecycleBinSettings settings)
        => new(services.GetMarshaller(), settings);

    protected override FrameworkElement CreateView(RecycleBinViewModel viewModel) => new RecycleBinView();
}