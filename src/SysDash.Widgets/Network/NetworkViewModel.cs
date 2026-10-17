using System.Net.Http;
using System.Windows.Threading;
using SysDash.Core.History;
using SysDash.Core.Models;
using SysDash.Core.Threading;
using SysDash.Core.Units;
using SysDash.Widgets.Common;

namespace SysDash.Widgets.Network;

/// <summary>
/// Detailed network widget: big upload/download rates, a mirrored history
/// graph (upload up, download down), adapter + status, local and public IP,
/// and the totals transferred since the widget started.
/// </summary>
public sealed class NetworkViewModel : HardwareWidgetViewModel
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(6) };

    private HistoryBuffer _down;
    private HistoryBuffer _up;
    private readonly DispatcherTimer _publicIpTimer;
    private DateTimeOffset? _lastSample;
    private string? _autoAdapter;
    private double _totalSent;
    private double _totalReceived;
    private double[] _downHistory = Array.Empty<double>();
    private double[] _upHistory = Array.Empty<double>();
    private string _downText = "—";
    private string _upText = "—";
    private string _peakDownText = "—";
    private string _peakUpText = "—";
    private string _adapterText = "No active adapter";
    private string _statusText = string.Empty;
    private string _ipText = "—";
    private string _publicIpText = "Looking up…";
    private string _totalSentText = "—";
    private string _totalReceivedText = "—";

    public NetworkViewModel(IThreadMarshaller marshaller, NetworkSettings settings)
        : base(marshaller, settings, settings.HistoryCapacity)
    {
        int capacity = Math.Clamp(settings.HistoryCapacity, 30, 900);
        _down = new HistoryBuffer(capacity);
        _up = new HistoryBuffer(capacity);

        _publicIpTimer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(30) };
        _publicIpTimer.Tick += async (_, _) => await RefreshPublicIpAsync();
    }

    public NetworkSettings Options => (NetworkSettings)Settings;

    public override string Title => "Network";

    public bool ShowPublicIp => Options.ShowPublicIp;

    public bool ShowIp => Options.ShowIp;

    public bool ShowTotals => Options.ShowTotals;

    public bool ShowGraph => Options.ShowGraph;

    protected override void OnSettingsChanged()
    {
        ResizeHistory(Options.HistoryCapacity);
        _down = Resize(_down, Options.HistoryCapacity);
        _up = Resize(_up, Options.HistoryCapacity);
        DownHistory = _down.ToArray();
        UpHistory = _up.ToArray();

        if (Options.ShowPublicIp)
        {
            _publicIpTimer.Start();
            _ = RefreshPublicIpAsync();
        }
        else
        {
            _publicIpTimer.Stop();
        }
    }

    private string Rate(double bytesPerSec)
        => Options.UseBits ? UnitFormat.BitsPerSecond(bytesPerSec) : UnitFormat.BytesPerSecond(bytesPerSec);

    public string DownText { get => _downText; private set => SetProperty(ref _downText, value); }

    public string UpText { get => _upText; private set => SetProperty(ref _upText, value); }

    public string PeakDownText { get => _peakDownText; private set => SetProperty(ref _peakDownText, value); }

    public string PeakUpText { get => _peakUpText; private set => SetProperty(ref _peakUpText, value); }

    public string AdapterText { get => _adapterText; private set => SetProperty(ref _adapterText, value); }

    public string StatusText { get => _statusText; private set => SetProperty(ref _statusText, value); }

    public string IpText { get => _ipText; private set => SetProperty(ref _ipText, value); }

    public string PublicIpText { get => _publicIpText; private set => SetProperty(ref _publicIpText, value); }

    public string TotalSentText { get => _totalSentText; private set => SetProperty(ref _totalSentText, value); }

    public string TotalReceivedText { get => _totalReceivedText; private set => SetProperty(ref _totalReceivedText, value); }

    public double[] DownHistory { get => _downHistory; private set => SetProperty(ref _downHistory, value); }

    public double[] UpHistory { get => _upHistory; private set => SetProperty(ref _upHistory, value); }

    protected override void OnActivated()
    {
        if (Options.ShowPublicIp)
        {
            _publicIpTimer.Start();
            _ = RefreshPublicIpAsync();
        }
    }

    protected override void OnDispose()
    {
        _publicIpTimer.Stop();
    }

    protected override void Update(MetricsSnapshot snapshot)
    {
        NetworkAdapterReading? adapter = Pick(snapshot.Adapters);

        if (adapter is null)
        {
            DownText = "—";
            UpText = "—";
            AdapterText = "No active adapter";
            StatusText = string.Empty;
            IpText = "—";
            return;
        }

        DownText = Rate(adapter.DownBytesPerSec);
        UpText = Rate(adapter.UpBytesPerSec);
        AdapterText = adapter.Name;
        StatusText = adapter.IsUp ? "Connected" : "Disconnected";
        IpText = adapter.IpV4 ?? "—";

        _down.Add(adapter.DownBytesPerSec);
        _up.Add(adapter.UpBytesPerSec);
        DownHistory = _down.ToArray();
        UpHistory = _up.ToArray();
        PeakDownText = Rate(_down.Max());
        PeakUpText = Rate(_up.Max());

        // Integrate rate x elapsed time for the session totals.
        if (_lastSample is { } last)
        {
            double seconds = Math.Clamp((snapshot.Timestamp - last).TotalSeconds, 0, 10);
            _totalSent += adapter.UpBytesPerSec * seconds;
            _totalReceived += adapter.DownBytesPerSec * seconds;
        }

        _lastSample = snapshot.Timestamp;
        TotalSentText = UnitFormat.Bytes(_totalSent);
        TotalReceivedText = UnitFormat.Bytes(_totalReceived);
    }

    private async Task RefreshPublicIpAsync()
    {
        try
        {
            string ip = (await Http.GetStringAsync("https://api.ipify.org")).Trim();
            PublicIpText = ip.Length is > 0 and < 64 ? ip : "Unavailable";
        }
        catch
        {
            PublicIpText = "Offline";
        }
    }

    private NetworkAdapterReading? Pick(IReadOnlyList<NetworkAdapterReading>? adapters)
    {
        if (adapters is null || adapters.Count == 0)
        {
            return null;
        }

        // A chosen adapter wins; empty = automatic (busiest adapter).
        if (!string.IsNullOrWhiteSpace(Options.AdapterName))
        {
            NetworkAdapterReading? named = adapters.FirstOrDefault(
                a => a.Name.Equals(Options.AdapterName, StringComparison.OrdinalIgnoreCase));
            if (named is not null)
            {
                return named;
            }
        }

        var active = adapters
            .Where(a => a.IsUp || a.DownBytesPerSec + a.UpBytesPerSec > 0)
            .ToList();

        var pool = active.Count > 0 ? active : adapters.ToList();
        // Real adapters before virtual ones (Hyper-V / WSL / VMware / VirtualBox /
        // VPN taps), so an idle PC does not lock onto "vEthernet (WSL)".
        NetworkAdapterReading? busiest = pool
            .OrderBy(a => IsVirtualAdapter(a.Name))
            .ThenByDescending(a => a.IpV4 is not null)
            .ThenByDescending(a => a.DownBytesPerSec + a.UpBytesPerSec)
            .FirstOrDefault();

        // Sticky choice: only switch adapters when another one is clearly busier,
        // so the name / IP do not flicker every second.
        NetworkAdapterReading? current = _autoAdapter is null
            ? null
            : pool.FirstOrDefault(a => a.Name == _autoAdapter && a.IsUp);
        if (current is not null && busiest is not null && !ReferenceEquals(current, busiest))
        {
            double currentRate = current.DownBytesPerSec + current.UpBytesPerSec;
            double busiestRate = busiest.DownBytesPerSec + busiest.UpBytesPerSec;
            if (busiestRate < Math.Max(currentRate * 2, 20_000))
            {
                return current;
            }
        }

        _autoAdapter = busiest?.Name;
        return busiest;
    }

    private static readonly string[] VirtualMarkers =
    {
        "vEthernet", "Hyper-V", "VMware", "VirtualBox", "Virtual", "WSL", "Docker", "TAP", "Npcap", "Loopback", "Bluetooth",
    };

    private static bool IsVirtualAdapter(string name)
        => VirtualMarkers.Any(m => name.Contains(m, StringComparison.OrdinalIgnoreCase));
}
