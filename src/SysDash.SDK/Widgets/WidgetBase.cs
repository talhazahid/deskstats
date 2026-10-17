using System.Windows;

namespace SysDash.SDK.Widgets;

/// <summary>
/// Convenience base for widget implementations with typed settings and view model.
/// </summary>
/// <typeparam name="TSettings">Widget settings POCO (must have parameterless ctor).</typeparam>
/// <typeparam name="TViewModel">Widget view model.</typeparam>
public abstract class WidgetBase<TSettings, TViewModel> : IWidget
    where TSettings : WidgetSettings, new()
    where TViewModel : WidgetViewModel
{
    public abstract string Id { get; }

    public abstract string DisplayName { get; }

    public abstract string Description { get; }

    public abstract string Category { get; }

    public virtual WidgetSettings CreateDefaultSettings() => new TSettings();

    public WidgetSettings? DeserializeSettings(string json)
        => WidgetSettingsSerializer.Deserialize(typeof(TSettings), json);

    public string SerializeSettings(WidgetSettings settings)
        => WidgetSettingsSerializer.Serialize(settings);

    public FrameworkElement CreateView(WidgetInstance instance)
    {
        var settings = instance.Settings as TSettings ?? new TSettings();
        instance.Settings = settings;

        var viewModel = CreateViewModel(instance.Services, settings);
        instance.ViewModel = viewModel;

        var view = CreateView(viewModel);
        view.DataContext = viewModel;
        instance.View = view;
        return view;
    }

    protected abstract TViewModel CreateViewModel(IServiceProvider services, TSettings settings);

    protected abstract FrameworkElement CreateView(TViewModel viewModel);
}