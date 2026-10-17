using System.Collections.ObjectModel;
using System.Globalization;
using System.Net.Http;
using System.Text.Json.Serialization;
using System.Windows.Threading;
using SysDash.Core.Models;
using SysDash.Core.Threading;
using SysDash.SDK.Widgets;

namespace SysDash.Widgets.Weather;

/* -------- Open-Meteo response DTOs (snake_case wire names) -------- */

public sealed class WeatherResponse
{
    [JsonPropertyName("latitude")] public double Latitude { get; set; }
    [JsonPropertyName("longitude")] public double Longitude { get; set; }
    [JsonPropertyName("timezone")] public string? Timezone { get; set; }
    [JsonPropertyName("current")] public WeatherCurrent? Current { get; set; }
    [JsonPropertyName("hourly")] public WeatherHourly? Hourly { get; set; }
    [JsonPropertyName("daily")] public WeatherDaily? Daily { get; set; }
}

public sealed class WeatherCurrent
{
    [JsonPropertyName("time")] public string? Time { get; set; }
    [JsonPropertyName("temperature_2m")] public double? Temperature2m { get; set; }
    [JsonPropertyName("relative_humidity_2m")] public double? RelativeHumidity2m { get; set; }
    [JsonPropertyName("apparent_temperature")] public double? ApparentTemperature { get; set; }
    [JsonPropertyName("weather_code")] public double? WeatherCode { get; set; }
    [JsonPropertyName("wind_speed_10m")] public double? WindSpeed10m { get; set; }
    [JsonPropertyName("wind_direction_10m")] public double? WindDirection10m { get; set; }
    [JsonPropertyName("pressure_msl")] public double? PressureMsl { get; set; }
    [JsonPropertyName("is_day")] public double? IsDay { get; set; }
}

public sealed class AirQualityResponse
{
    [JsonPropertyName("current")] public AirQualityCurrent? Current { get; set; }
}

public sealed class AirQualityCurrent
{
    [JsonPropertyName("us_aqi")] public double? UsAqi { get; set; }
    [JsonPropertyName("pm2_5")] public double? Pm25 { get; set; }
}

public sealed class WeatherHourly
{
    [JsonPropertyName("time")] public List<string>? Time { get; set; }
    [JsonPropertyName("temperature_2m")] public List<double?>? Temperature2m { get; set; }
    [JsonPropertyName("weather_code")] public List<double?>? WeatherCode { get; set; }
    [JsonPropertyName("precipitation_probability")] public List<double?>? PrecipitationProbability { get; set; }
    [JsonPropertyName("uv_index")] public List<double?>? UvIndex { get; set; }
    [JsonPropertyName("visibility")] public List<double?>? Visibility { get; set; }
    [JsonPropertyName("dew_point_2m")] public List<double?>? DewPoint2m { get; set; }
}

public sealed class WeatherDaily
{
    [JsonPropertyName("time")] public List<string>? Time { get; set; }
    [JsonPropertyName("temperature_2m_max")] public List<double?>? TemperatureMax { get; set; }
    [JsonPropertyName("temperature_2m_min")] public List<double?>? TemperatureMin { get; set; }
    [JsonPropertyName("precipitation_probability_max")] public List<double?>? PrecipitationMax { get; set; }
    [JsonPropertyName("weather_code")] public List<double?>? WeatherCode { get; set; }
}

/// <summary>
/// Detailed weather widget (Open-Meteo, no API key): big temperature + icon,
/// a details grid (wind, feels like, dew point, UV, pressure, humidity,
/// visibility), an hourly strip with rain chance, and today's summary.
/// </summary>
public sealed class WeatherViewModel : WidgetViewModel
{
    private static readonly HttpClient Http = new()
    {
        Timeout = TimeSpan.FromSeconds(8),
    };

    private static readonly string[] Compass = { "↓", "↙", "←", "↖", "↑", "↗", "→", "↘" };

    private readonly DispatcherTimer _refreshTimer;
    private readonly ObservableCollection<HourlyItem> _hourly = new();
    private readonly ObservableCollection<DayItem> _days = new();
    private string _airText = "—";
    private string _rainChanceText = "—";
    private bool _hasData;
    private string _tempText = "—";
    private string _conditionText = "Loading…";
    private string _iconEmoji = "☁";
    private string _feelsLikeText = "—";
    private string _windText = "—";
    private string _dewPointText = "—";
    private string _pressureText = "—";
    private string _uvText = "—";
    private string _humidityText = "—";
    private string _visibilityText = "—";
    private string _todayIcon = string.Empty;
    private string _todayPrecipText = string.Empty;
    private string _todayRangeText = string.Empty;
    private string _todaySummary = string.Empty;
    private string _updatedText = string.Empty;
    private bool _refreshing;
    private bool _refreshAgain;
    private bool _savingResolvedLocation;

    public WeatherViewModel(IThreadMarshaller marshaller, WeatherSettings settings)
        : base(marshaller, settings)
    {
        _refreshTimer = new DispatcherTimer { Interval = RefreshInterval() };
        _refreshTimer.Tick += (_, _) => _ = RefreshAsync();
    }

    public WeatherSettings Options => (WeatherSettings)Settings;

    public override string Title => "Weather";

    /// <summary>City as a proper name ("Karachi"), whatever case it was typed in.</summary>
    public string LocationText => TitleCase(Options.LocationName);

    private static string TitleCase(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        string trimmed = text.Trim();
        // Only fix all-lower / all-upper input; keep names like "McAllen" as given.
        return trimmed == trimmed.ToLowerInvariant() || trimmed == trimmed.ToUpperInvariant()
            ? CultureInfo.InvariantCulture.TextInfo.ToTitleCase(trimmed.ToLowerInvariant())
            : trimmed;
    }

    public bool ShowHourly => Options.ShowHourly;

    public bool HasData { get => _hasData; private set => SetProperty(ref _hasData, value); }

    public string TempText { get => _tempText; private set => SetProperty(ref _tempText, value); }

    public string ConditionText { get => _conditionText; private set => SetProperty(ref _conditionText, value); }

    public string IconEmoji { get => _iconEmoji; private set => SetProperty(ref _iconEmoji, value); }

    public string FeelsLikeText { get => _feelsLikeText; private set => SetProperty(ref _feelsLikeText, value); }

    public string WindText { get => _windText; private set => SetProperty(ref _windText, value); }

    public string DewPointText { get => _dewPointText; private set => SetProperty(ref _dewPointText, value); }

    public string PressureText { get => _pressureText; private set => SetProperty(ref _pressureText, value); }

    /// <summary>US AQI with a plain label, e.g. "42 · Good" (Open-Meteo air quality).</summary>
    public string AirText { get => _airText; private set => SetProperty(ref _airText, value); }

    /// <summary>Chance of rain in the coming hour.</summary>
    public string RainChanceText { get => _rainChanceText; private set => SetProperty(ref _rainChanceText, value); }

    public ObservableCollection<DayItem> Days => _days;

    public bool ShowDays => Options.ShowDaily && _days.Count > 0;

    public string UvText { get => _uvText; private set => SetProperty(ref _uvText, value); }

    public string HumidityText { get => _humidityText; private set => SetProperty(ref _humidityText, value); }

    public string VisibilityText { get => _visibilityText; private set => SetProperty(ref _visibilityText, value); }

    public string TodayIcon { get => _todayIcon; private set => SetProperty(ref _todayIcon, value); }

    public string TodayPrecipText { get => _todayPrecipText; private set => SetProperty(ref _todayPrecipText, value); }

    public string TodayRangeText { get => _todayRangeText; private set => SetProperty(ref _todayRangeText, value); }

    public string TodaySummary { get => _todaySummary; private set => SetProperty(ref _todaySummary, value); }

    public string UpdatedText { get => _updatedText; private set => SetProperty(ref _updatedText, value); }

    public ObservableCollection<HourlyItem> Hourly => _hourly;

    protected override void OnActivated()
    {
        _refreshTimer.Interval = RefreshInterval();
        _refreshTimer.Start();
        _ = RefreshAsync();
    }

    protected override void OnDispose()
    {
        _refreshTimer.Stop();
    }

    protected override void OnSettingsChanged()
    {
        _refreshTimer.Interval = RefreshInterval();
        OnPropertyChanged(nameof(ShowDays));
        if (!_savingResolvedLocation)
        {
            _ = RefreshAsync(); // new city / units → refresh right away
        }
    }

    /// <summary>
    /// Turns the typed city name into coordinates with Open-Meteo's free
    /// geocoding API (no key), then saves them with the widget settings.
    /// Returns false when the city could not be found.
    /// </summary>
    private async Task<bool> ResolveCityAsync(string city)
    {
        string url = "https://geocoding-api.open-meteo.com/v1/search?count=1&language=en&format=json&name="
                     + Uri.EscapeDataString(city);
        string json = await Http.GetStringAsync(url);

        using System.Text.Json.JsonDocument doc = System.Text.Json.JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("results", out System.Text.Json.JsonElement results) ||
            results.GetArrayLength() == 0)
        {
            return false;
        }

        System.Text.Json.JsonElement first = results[0];
        Options.Latitude = first.GetProperty("latitude").GetDouble();
        Options.Longitude = first.GetProperty("longitude").GetDouble();
        Options.ResolvedLocation = city;

        _savingResolvedLocation = true;
        try
        {
            NotifySettingsChanged(); // persist the coordinates
        }
        finally
        {
            _savingResolvedLocation = false;
        }

        return true;
    }

    protected override void Update(MetricsSnapshot snapshot)
    {
        // Weather is fetched independently; no need for the hardware snapshot.
    }

    private TimeSpan RefreshInterval()
    {
        int minutes = Math.Clamp(Options.RefreshMinutes, 10, 180);
        return TimeSpan.FromMinutes(minutes);
    }

    private async Task RefreshAsync()
    {
        if (_refreshing)
        {
            _refreshAgain = true; // settings changed mid-refresh → run again right after
            return;
        }

        _refreshing = true;
        try
        {
            string city = (Options.LocationName ?? string.Empty).Trim();
            if (city.Length > 0 &&
                !string.Equals(city, Options.ResolvedLocation, StringComparison.OrdinalIgnoreCase) &&
                !await ResolveCityAsync(city))
            {
                ConditionText = "City not found";
                UpdatedText = $"Could not find \"{city}\" — check the spelling in Settings";
                return;
            }

            CultureInfo inv = CultureInfo.InvariantCulture;
            string url =
                "https://api.open-meteo.com/v1/forecast" +
                $"?latitude={Options.Latitude.ToString(inv)}" +
                $"&longitude={Options.Longitude.ToString(inv)}" +
                "&current=temperature_2m,relative_humidity_2m,apparent_temperature,weather_code,wind_speed_10m,wind_direction_10m,pressure_msl,is_day" +
                "&hourly=temperature_2m,weather_code,precipitation_probability,uv_index,visibility,dew_point_2m" +
                "&daily=temperature_2m_max,temperature_2m_min,precipitation_probability_max,weather_code" +
                "&forecast_hours=7&forecast_days=7&timezone=auto";

            using HttpResponseMessage response = await Http.GetAsync(url);
            response.EnsureSuccessStatusCode();

            string json = await response.Content.ReadAsStringAsync();
            WeatherResponse? data = System.Text.Json.JsonSerializer.Deserialize<WeatherResponse>(json, Core.Serialization.JsonDefaults.Options);
            if (data?.Current is not { } cur)
            {
                return;
            }

            TempText = FormatTemp(cur.Temperature2m ?? 0, decimals: true);
            HasData = true;

            (string label, string emoji) = WeatherCode.Describe(cur.WeatherCode, night: cur.IsDay is 0);
            ConditionText = label;
            IconEmoji = emoji;

            FeelsLikeText = cur.ApparentTemperature is { } feels ? FormatTemp(feels, decimals: true) : "—";
            HumidityText = cur.RelativeHumidity2m is { } h ? $"{h:0}%" : "—";
            PressureText = cur.PressureMsl is { } hpa ? $"{hpa.ToString("#,0", inv)} hPa" : "—";
            WindText = cur.WindSpeed10m is { } w
                ? $"{w:0} km/h {Arrow(cur.WindDirection10m)}".TrimEnd()
                : "—";

            WeatherHourly? hourly = data.Hourly;
            RainChanceText = First(hourly?.PrecipitationProbability) is { } pop ? $"{pop:0}%" : "—";
            UvText = First(hourly?.UvIndex) is { } uv ? uv.ToString("0", inv) : "—";
            DewPointText = First(hourly?.DewPoint2m) is { } dew ? FormatTemp(dew, decimals: true) : "—";
            VisibilityText = First(hourly?.Visibility) is { } vis
                ? (vis >= 1000 ? $"{(vis / 1000).ToString("0", inv)} km" : $"{vis:0} m")
                : "—";

            _hourly.Clear();
            if (Options.ShowHourly && hourly?.Time is { } times && hourly.Temperature2m is { } temps)
            {
                for (int i = 0; i < times.Count && i < temps.Count && _hourly.Count < 6; i++)
                {
                    if (temps[i] is not { } t)
                    {
                        continue;
                    }

                    (_, string hourEmoji) = WeatherCode.Describe(At(hourly.WeatherCode, i));
                    string precip = At(hourly.PrecipitationProbability, i) is { } pp ? $"{pp:0}%" : string.Empty;
                    _hourly.Add(new HourlyItem(ShortHourLabel(times[i]), FormatTemp(t, decimals: false), hourEmoji, precip));
                }
            }

            WeatherDaily? daily = data.Daily;
            (string todayLabel, string todayEmoji) = WeatherCode.Describe(First(daily?.WeatherCode) ?? cur.WeatherCode);
            TodayIcon = todayEmoji;
            double? rain = First(daily?.PrecipitationMax);
            TodayPrecipText = rain is { } r ? $"{r:0}%" : string.Empty;
            TodayRangeText = First(daily?.TemperatureMin) is { } min && First(daily?.TemperatureMax) is { } max
                ? $"{FormatTemp(min, decimals: false)}  {FormatTemp(max, decimals: false)}"
                : string.Empty;
            TodaySummary = rain is >= 50
                ? $"{todayLabel}. Rain likely today."
                : rain is >= 20
                    ? $"{todayLabel}. Possible rain later."
                    : $"{todayLabel}.";

            // Next days (tomorrow onwards): day name, icon, high / low.
            _days.Clear();
            if (daily?.Time is { } dayTimes)
            {
                int dayCount = Math.Clamp(Options.DailyDays, 1, 6);
                for (int i = 1; i < dayTimes.Count && _days.Count < dayCount; i++)
                {
                    string dayName = DateTime.TryParse(dayTimes[i], CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime day)
                        ? day.ToString("ddd", CultureInfo.InvariantCulture)
                        : dayTimes[i];
                    (_, string dayEmoji) = WeatherCode.Describe(At(daily.WeatherCode, i));
                    string high = At(daily.TemperatureMax, i) is { } hi ? FormatTemp(hi, decimals: false) : "—";
                    string low = At(daily.TemperatureMin, i) is { } lo ? FormatTemp(lo, decimals: false) : "—";
                    _days.Add(new DayItem(dayName, dayEmoji, high, low));
                }
            }

            OnPropertyChanged(nameof(ShowDays));

            await RefreshAirQualityAsync();

            UpdatedText = $"Updated {DateTime.Now:HH:mm}";
            _refreshTimer.Interval = RefreshInterval();
        }
        catch (Exception)
        {
            // Keep the last good values; try again on a shorter timer.
            UpdatedText = "Offline — retrying";
            if (!HasData)
            {
                ConditionText = "Offline";
            }

            _refreshTimer.Interval = TimeSpan.FromMinutes(2);
        }
        finally
        {
            _refreshing = false;
            if (_refreshAgain)
            {
                _refreshAgain = false;
                _ = RefreshAsync();
            }
        }
    }

    /// <summary>Air quality (US AQI) from Open-Meteo's free air-quality API; optional.</summary>
    private async Task RefreshAirQualityAsync()
    {
        try
        {
            CultureInfo inv = CultureInfo.InvariantCulture;
            string url = "https://air-quality-api.open-meteo.com/v1/air-quality" +
                         $"?latitude={Options.Latitude.ToString(inv)}&longitude={Options.Longitude.ToString(inv)}" +
                         "&current=us_aqi,pm2_5&timezone=auto";
            string json = await Http.GetStringAsync(url);
            AirQualityResponse? air = System.Text.Json.JsonSerializer.Deserialize<AirQualityResponse>(json, Core.Serialization.JsonDefaults.Options);
            AirText = air?.Current?.UsAqi is { } aqi ? $"{aqi:0} · {AqiLabel(aqi)}" : "—";
        }
        catch
        {
            AirText = "—"; // optional data: never break the widget
        }
    }

    private static string AqiLabel(double aqi) => aqi switch
    {
        <= 50 => "Good",
        <= 100 => "Moderate",
        <= 150 => "Unhealthy for sensitive groups",
        <= 200 => "Unhealthy",
        <= 300 => "Very unhealthy",
        _ => "Hazardous",
    };

    private static double? First(List<double?>? values) => values is { Count: > 0 } ? values[0] : null;

    private static double? At(List<double?>? values, int index)
        => values is not null && index < values.Count ? values[index] : null;

    private static string Arrow(double? degrees)
    {
        if (degrees is not { } d)
        {
            return string.Empty;
        }

        // Meteorological direction = where the wind comes FROM; arrow shows where it goes.
        int sector = (int)Math.Round(((d % 360) + 360) % 360 / 45.0) % 8;
        return Compass[sector];
    }

    private string FormatTemp(double celsius, bool decimals)
    {
        double value = Options.UseFahrenheit ? celsius * 1.8 + 32 : celsius;
        return decimals
            ? $"{value.ToString("0.0", CultureInfo.InvariantCulture)}°"
            : $"{value.ToString("0", CultureInfo.InvariantCulture)}°";
    }

    private static string ShortHourLabel(string isoTime)
    {
        if (DateTime.TryParse(isoTime, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime dt))
        {
            return dt.ToString("HH:mm", CultureInfo.InvariantCulture);
        }

        return isoTime;
    }
}

/// <summary>WMO weather-code → human label + emoji mapping.</summary>
public static class WeatherCode
{
    public static (string Label, string Emoji) Describe(double? code, bool night = false)
    {
        // Clear / mostly clear at night: moon instead of sun.
        if (night && code is 0)
        {
            return ("Clear", "🌙");
        }

        if (night && code is 1 or 2)
        {
            return (code is 1 ? "Mostly clear" : "Partly cloudy", "☁️");
        }

        return code switch
        {
            null => ("—", "🌡️"),
            0 => ("Clear", "☀️"),
            1 => ("Mostly clear", "🌤️"),
            2 => ("Partly cloudy", "⛅"),
            3 => ("Overcast", "☁️"),
            45 or 48 => ("Fog", "🌫️"),
            51 or 53 or 55 => ("Drizzle", "🌦️"),
            56 or 57 => ("Freezing drizzle", "🌧️"),
            61 or 63 or 65 => ("Rain", "🌧️"),
            66 or 67 => ("Freezing rain", "🌧️"),
            71 or 73 or 75 => ("Snow", "🌨️"),
            77 => ("Snow grains", "🌨️"),
            80 or 81 or 82 => ("Rain showers", "🌧️"),
            85 or 86 => ("Snow showers", "🌨️"),
            95 => ("Thunderstorm", "⛈️"),
            96 or 99 => ("Hail storm", "⛈️"),
            _ => ("—", "🌡️"),
        };
    }
}