using System.Collections.ObjectModel;
using System.Windows.Threading;
using SysDash.Core.Models;
using SysDash.Core.Threading;
using SysDash.Core.Mvvm;
using SysDash.SDK.Widgets;

namespace SysDash.Widgets.Timezone;

public sealed class TimezoneViewModel : WidgetViewModel
{
    private static readonly string[] FallbackZones =
    {
        "UTC", "Eastern Standard Time", "Central European Standard Time", "Tokyo Standard Time",
    };

    private readonly DispatcherTimer _timer;
    private readonly ObservableCollection<ZoneTime> _zones = new();
    private readonly List<string> _zoneIds = new();

    public TimezoneViewModel(IThreadMarshaller marshaller, TimezoneSettings settings)
        : base(marshaller, settings)
    {
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _timer.Tick += (_, _) => Refresh();

        List<string> ids = settings.TimeZoneIds is { Count: > 0 } list ? list : FallbackZones.ToList();
        foreach (string id in ids)
        {
            _zoneIds.Add(id);
            _zones.Add(new ZoneTime(ConvertLabel(id)));
        }
    }

    public TimezoneSettings Options => (TimezoneSettings)Settings;

    public override string Title => "World Clock";

    public ObservableCollection<ZoneTime> Zones => _zones;

    protected override void OnActivated()
    {
        Refresh();
        _timer.Start();
    }

    protected override void OnDispose()
    {
        _timer.Stop();
    }

    protected override void Update(MetricsSnapshot snapshot)
    {
        // Timer-driven; snapshot ignored.
    }

    private void Refresh()
    {
        DateTime utc = DateTime.UtcNow;
        string format = Options.ShowSeconds ? "HH:mm:ss" : "HH:mm";

        for (int i = 0; i < _zones.Count && i < _zoneIds.Count; i++)
        {
            ZoneTime zone = _zones[i];
            try
            {
                TimeZoneInfo tzi = TimeZoneInfo.FindSystemTimeZoneById(_zoneIds[i]);
                DateTime local = TimeZoneInfo.ConvertTimeFromUtc(utc, tzi);
                zone.Time = local.ToString(format);
            }
            catch (TimeZoneNotFoundException)
            {
                zone.Time = "—";
            }
            catch (InvalidTimeZoneException)
            {
                zone.Time = "—";
            }
        }
    }

    private static string ConvertLabel(string timeZoneId)
    {
        int slash = timeZoneId.IndexOf('/');
        return slash >= 0 ? timeZoneId.Substring(slash + 1).Replace('_', ' ') : timeZoneId;
    }
}