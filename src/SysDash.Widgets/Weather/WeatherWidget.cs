using System.Windows;
using SysDash.SDK.Widgets;
using SysDash.Widgets.Common;

namespace SysDash.Widgets.Weather;

[Widget("weather", "Weather", Description = "Current conditions, air quality and forecast. Weather data by Open-Meteo.com",
    Category = WidgetCategories.Internet)]
public sealed class WeatherWidget : WidgetBase<WeatherSettings, WeatherViewModel>
{
    public override string Id => "weather";

    public override string DisplayName => "Weather";

    public override string Description => "Current conditions, air quality, hourly and daily forecast. Weather data by Open-Meteo.com (CC BY 4.0).";

    public override string Category => WidgetCategories.Internet;

    protected override WeatherViewModel CreateViewModel(IServiceProvider services, WeatherSettings settings)
        => new(services.GetMarshaller(), settings);

    protected override FrameworkElement CreateView(WeatherViewModel viewModel) => new WeatherView();
}