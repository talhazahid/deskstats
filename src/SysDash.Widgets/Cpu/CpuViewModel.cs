using System.Collections.ObjectModel;
using System.Text.RegularExpressions;
using SysDash.Core.Diagnostics;
using SysDash.Core.Mvvm;
using SysDash.Core.Models;
using SysDash.Core.Threading;
using SysDash.Core.Units;
using SysDash.Widgets.Common;

namespace SysDash.Widgets.Cpu;

/// <summary>
/// Detailed CPU widget: load history bars, one ring per logical core, and a
/// details list (temperature, clock, package power, voltage, fan).
/// </summary>
public sealed class CpuViewModel : HardwareWidgetViewModel
{
    private static readonly char[] SplitChars = { '@' };
    private readonly ObservableCollection<CoreItem> _cores = new();
    private double _loadPercent;
    private string _loadText = "—";
    private string _headerText = string.Empty;
    private string _tempText = "—";
    private string _clockText = "—";
    private string _powerText = "—";
    private string _voltageText = "—";
    private string _fanText = "—";
    private string _nameText = string.Empty;
    private string _coresSummary = string.Empty;
    private bool _isTempAlert;
    private string _driverHint = string.Empty;
    private string _installButtonText = "Install sensor driver";
    private string _alertText = string.Empty;

    public CpuViewModel(IThreadMarshaller marshaller, CpuSettings settings)
        : base(marshaller, settings, settings.HistoryCapacity)
    {
        InstallDriverCommand = new RelayCommand(() => _ = InstallDriverAsync());
    }

    /// <summary>Why CPU sensors are missing (no PawnIO driver / not admin), or empty.</summary>
    public string DriverHint { get => _driverHint; private set => SetProperty(ref _driverHint, value); }

    public bool HasDriverHint => _driverHint.Length > 0;

    /// <summary>Installs the PawnIO sensor driver (DeskStats restarts afterwards).</summary>
    public RelayCommand InstallDriverCommand { get; }

    public string InstallButtonText { get => _installButtonText; private set => SetProperty(ref _installButtonText, value); }

    private async Task InstallDriverAsync()
    {
        InstallButtonText = "Installing…";
        bool ok = await SensorDriver.InstallPawnIOAsync();
        InstallButtonText = ok ? "Installed — restarting…" : "Open download page";
    }

    private void ApplyDriverHint(string? hint)
    {
        DriverHint = hint ?? string.Empty;
        OnPropertyChanged(nameof(HasDriverHint));
    }


    public CpuSettings Options => (CpuSettings)Settings;

    public override string Title => "CPU";

    public double LoadPercent
    {
        get => _loadPercent;
        private set => SetProperty(ref _loadPercent, value);
    }

    public string LoadText
    {
        get => _loadText;
        private set => SetProperty(ref _loadText, value);
    }

    /// <summary>"3.85 GHz · 61°C" shown in the header of the graph section.</summary>
    public string HeaderText
    {
        get => _headerText;
        private set => SetProperty(ref _headerText, value);
    }

    public string TempText
    {
        get => _tempText;
        private set => SetProperty(ref _tempText, value);
    }

    public string ClockText
    {
        get => _clockText;
        private set => SetProperty(ref _clockText, value);
    }

    public string PowerText
    {
        get => _powerText;
        private set => SetProperty(ref _powerText, value);
    }

    public string VoltageText
    {
        get => _voltageText;
        private set => SetProperty(ref _voltageText, value);
    }

    public string FanText
    {
        get => _fanText;
        private set => SetProperty(ref _fanText, value);
    }

    public string NameText
    {
        get => _nameText;
        private set => SetProperty(ref _nameText, value);
    }

    public string CoresSummary
    {
        get => _coresSummary;
        private set => SetProperty(ref _coresSummary, value);
    }

    public bool HasPerCore => Options.ShowPerCore && _cores.Count > 0;

    public bool ShowGraph => Options.ShowGraph;

    public bool ShowDetails => Options.ShowDetails;

    public bool IsTempAlert { get => _isTempAlert; private set => SetProperty(ref _isTempAlert, value); }

    public string AlertText { get => _alertText; private set => SetProperty(ref _alertText, value); }

    protected override void OnSettingsChanged()
    {
        ResizeHistory(Options.HistoryCapacity);
        OnPropertyChanged(nameof(GraphCapacity));
    }

    public ObservableCollection<CoreItem> Cores => _cores;

    protected override void Update(MetricsSnapshot snapshot)
    {
        CpuReading? cpu = snapshot.Cpu;

        if (cpu is null)
        {
            LoadText = "—";
            HeaderText = string.Empty;
            PushHistory(0);
            return;
        }

        NameText = ShortenName(cpu.Name);

        double load = Math.Clamp(cpu.Load ?? 0, 0, 100);
        LoadPercent = load;
        LoadText = $"{load:0}%";
        PushHistory(load);

        string? temp = cpu.TemperatureC is { } t ? UnitFormat.Temperature(t, Options.UseFahrenheit) : null;
        string? clock = cpu.ClockMhz is { } mhz ? UnitFormat.Clock(mhz, gigahertz: true) : null;

        TempText = temp is null ? "—" : cpu.ApproximateTemperature ? $"≈ {temp} (ACPI)" : temp;
        ApplyDriverHint(snapshot.SensorHint);
        IsTempAlert = cpu.TemperatureC is { } hot && hot >= Options.TempAlertC;
        AlertText = IsTempAlert && temp is not null ? $"HOT {temp}" : string.Empty;
        ClockText = clock ?? "—";
        PowerText = cpu.PackagePowerW is { } w ? $"{w:0.0} W" : "—";
        VoltageText = cpu.Voltage is { } v && v > 0 ? UnitFormat.Voltage(v) : "—";
        FanText = cpu.FanRpm is { } rpm && rpm > 0 ? UnitFormat.Rpm(rpm) : "—";

        var header = new List<string>();
        if (Options.ShowClock && clock is not null)
        {
            header.Add(clock);
        }

        if (Options.ShowTemperature && temp is not null)
        {
            header.Add(temp);
        }

        HeaderText = string.Join(" · ", header);

        if (Options.ShowPerCore && cpu.PerCoreLoad is { Count: > 0 } coreLoads)
        {
            int shown = Math.Clamp(Options.MaxCoresShown, 1, coreLoads.Count);
            while (_cores.Count > shown)
            {
                _cores.RemoveAt(_cores.Count - 1);
            }

            while (_cores.Count < shown)
            {
                _cores.Add(new CoreItem());
            }

            double busiest = coreLoads.Count > 0 ? Math.Clamp(coreLoads.Max(), 0, 100) : 0;
            for (int i = 0; i < _cores.Count; i++)
            {
                double coreLoad = Math.Clamp(coreLoads[i], 0, 100);
                _cores[i].Name = $"Core {i + 1}: {coreLoad:0}%";
                _cores[i].Load = coreLoad;
                // Clocks are per PHYSICAL core, loads per THREAD: only pair them
                // when the counts match 1:1 (no SMT, no hybrid ambiguity).
                if (cpu.PerCoreClockMhz is { Count: > 0 } clocks && clocks.Count == coreLoads.Count && i < clocks.Count)
                {
                    _cores[i].ClockMhz = clocks[i];
                }
                else
                {
                    _cores[i].ClockMhz = 0;
                }
            }

            CoresSummary = $"{coreLoads.Count} threads · max {busiest:0}%";
        }
        else if (_cores.Count > 0)
        {
            _cores.Clear();
        }

        OnPropertiesChanged(nameof(HasPerCore));
    }

    private static string ShortenName(string name)
    {
        // "AMD Ryzen 5 5600X 6-Core Processor" -> "AMD Ryzen 5 5600X"
        string shortName = name.Split(SplitChars)[0];
        shortName = Regex.Replace(
            shortName, @"\s+\d+-Core\s+Processor|\s+Processor|\(R\)|\(TM\)|\s+CPU", string.Empty,
            RegexOptions.IgnoreCase);
        return shortName.Trim();
    }
}
