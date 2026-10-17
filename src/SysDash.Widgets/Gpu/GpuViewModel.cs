using System.Globalization;
using SysDash.Core.Models;
using SysDash.Core.Threading;
using SysDash.Core.Units;
using SysDash.Widgets.Common;

namespace SysDash.Widgets.Gpu;

/// <summary>
/// Detailed GPU widget: load history bars, four rings (load / VRAM / temperature
/// / core clock) and a details list.
/// </summary>
public sealed class GpuViewModel : HardwareWidgetViewModel
{
    private double _maxClockSeen = 1;
    private string _nameText = string.Empty;
    private double _loadPercent;
    private string _loadText = "—";
    private double _vramPercent;
    private string _vramPercentText = "—";
    private double _tempPercent;
    private string _tempShortText = "—";
    private double _clockPercent;
    private string _clockShortText = "—";
    private string _vramText = "—";
    private string _tempText = "—";
    private string _clockText = "—";
    private string _memClockText = "—";
    private string _fanText = "—";
    private string _voltageText = "—";
    private bool _isTempAlert;
    private string _alertText = string.Empty;

    public GpuViewModel(IThreadMarshaller marshaller, GpuSettings settings)
        : base(marshaller, settings, settings.HistoryCapacity)
    {
    }

    public GpuSettings Options => (GpuSettings)Settings;

    public override string Title => "GPU";

    public string NameText { get => _nameText; private set => SetProperty(ref _nameText, value); }

    public double LoadPercent { get => _loadPercent; private set => SetProperty(ref _loadPercent, value); }

    public string LoadText { get => _loadText; private set => SetProperty(ref _loadText, value); }

    public double VramPercent { get => _vramPercent; private set => SetProperty(ref _vramPercent, value); }

    public string VramPercentText { get => _vramPercentText; private set => SetProperty(ref _vramPercentText, value); }

    public double TempPercent { get => _tempPercent; private set => SetProperty(ref _tempPercent, value); }

    public string TempShortText { get => _tempShortText; private set => SetProperty(ref _tempShortText, value); }

    public double ClockPercent { get => _clockPercent; private set => SetProperty(ref _clockPercent, value); }

    public string ClockShortText { get => _clockShortText; private set => SetProperty(ref _clockShortText, value); }

    public string VramText { get => _vramText; private set => SetProperty(ref _vramText, value); }

    public string TempText { get => _tempText; private set => SetProperty(ref _tempText, value); }

    public string ClockText { get => _clockText; private set => SetProperty(ref _clockText, value); }

    public string MemClockText { get => _memClockText; private set => SetProperty(ref _memClockText, value); }

    public string FanText { get => _fanText; private set => SetProperty(ref _fanText, value); }

    public string VoltageText { get => _voltageText; private set => SetProperty(ref _voltageText, value); }

    public bool ShowGraph => Options.ShowGraph;

    public bool ShowRings => Options.ShowRings;

    public bool ShowDetails => Options.ShowDetails;

    public bool ShowVram => Options.ShowVram;

    public bool ShowTemperature => Options.ShowTemperature;

    public bool ShowClock => Options.ShowClock;

    public bool IsTempAlert { get => _isTempAlert; private set => SetProperty(ref _isTempAlert, value); }

    public string AlertText { get => _alertText; private set => SetProperty(ref _alertText, value); }

    protected override void OnSettingsChanged()
    {
        ResizeHistory(Options.HistoryCapacity);
    }

    protected override void Update(MetricsSnapshot snapshot)
    {
        GpuReading? gpu = Pick(snapshot.Gpus);

        if (gpu is null)
        {
            NameText = "No GPU sensors";
            LoadText = "—";
            PushHistory(0);
            return;
        }

        NameText = ShortenName(gpu.Name);

        double load = Math.Clamp(gpu.CoreLoad ?? 0, 0, 100);
        LoadPercent = load;
        LoadText = $"{load:0}%";
        PushHistory(load);

        if (gpu.VramPercent is { } vram)
        {
            VramPercent = Math.Clamp(vram, 0, 100);
            VramPercentText = $"{vram:0}%";
        }
        else
        {
            VramPercent = 0;
            VramPercentText = "—";
        }

        VramText = gpu.VramUsedBytes is { } used && gpu.VramTotalBytes is { } total && total > 0
            ? $"{UnitFormat.Bytes(used)} / {UnitFormat.Bytes(total)}"
            : (gpu.VramPercent is { } p ? $"{p:0}%" : "—");

        if (gpu.TemperatureC is { } temp)
        {
            TempPercent = Math.Clamp(temp, 0, 100);
            double shown = Options.UseFahrenheit ? temp * 1.8 + 32 : temp;
            TempShortText = $"{shown:0}°";
            TempText = UnitFormat.Temperature(temp, Options.UseFahrenheit);
            IsTempAlert = temp >= Options.TempAlertC;
            AlertText = IsTempAlert ? $"HOT {shown:0}°" : string.Empty;
        }
        else
        {
            TempPercent = 0;
            TempShortText = "—";
            TempText = "—";
            IsTempAlert = false;
        }

        if (gpu.CoreClockMhz is { } mhz)
        {
            _maxClockSeen = Math.Max(_maxClockSeen, mhz);
            ClockPercent = Math.Clamp(mhz / _maxClockSeen * 100, 0, 100);
            ClockShortText = (mhz / 1000).ToString("0.00", CultureInfo.InvariantCulture);
            ClockText = UnitFormat.MegaHertz(mhz);
        }
        else
        {
            ClockPercent = 0;
            ClockShortText = "—";
            ClockText = "—";
        }

        MemClockText = gpu.VramClockMhz is { } mem ? UnitFormat.MegaHertz(mem) : "—";
        FanText = gpu.FanPercent is { } fan ? $"{fan:0}%" : "—";
        VoltageText = gpu.Voltage is { } v && v > 0 ? UnitFormat.Voltage(v) : "—";
    }

    private GpuReading? Pick(IReadOnlyList<GpuReading>? gpus)
    {
        if (gpus is null || gpus.Count == 0)
        {
            return null;
        }

        int index = Math.Clamp(Options.GpuIndex, 0, gpus.Count - 1);
        return gpus[index];
    }

    private static string ShortenName(string name)
    {
        foreach (string marker in new[] { "GeForce", "Radeon", "Arc", "NVIDIA", "AMD", "Intel" })
        {
            int idx = name.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (idx >= 0)
            {
                return name.Substring(idx);
            }
        }

        return name;
    }
}
