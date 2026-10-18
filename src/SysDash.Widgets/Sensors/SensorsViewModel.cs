using System.Globalization;
using SysDash.Core.Diagnostics;
using SysDash.Core.Models;
using SysDash.Core.Mvvm;
using SysDash.Core.Threading;
using SysDash.Core.Units;
using SysDash.SDK.Widgets;

namespace SysDash.Widgets.Sensors;

/// <summary>
/// Sensors widget: CPU / GPU temperature rings, a fan ring, then the full list
/// of temperature sensors (hottest first) and fan speeds.
/// </summary>
public sealed class SensorsViewModel : WidgetViewModel
{
    private double _cpuTempPercent;
    private string _cpuTempText = "—";
    private double _gpuTempPercent;
    private string _gpuTempText = "—";
    private double _fanPercent;
    private string _fanText = "OFF";
    private IReadOnlyList<SensorRow> _temperatures = Array.Empty<SensorRow>();
    private IReadOnlyList<SensorRow> _fans = Array.Empty<SensorRow>();
    private bool _isTempAlert;
    private string _driverHint = string.Empty;
    private string _installButtonText = "Install sensor driver";
    private string _alertText = string.Empty;

    public SensorsViewModel(IThreadMarshaller marshaller, SensorsSettings settings)
        : base(marshaller, settings)
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
        InstallButtonText = ok ? "Installed — restarting…" : PawnIOInstaller.RestartRequired ? "Restart Windows" : "Retry driver installation";
        if (!ok) ApplyDriverHint(PawnIOInstaller.LastMessage);
    }

    private void ApplyDriverHint(string? hint)
    {
        DriverHint = hint ?? string.Empty;
        OnPropertyChanged(nameof(HasDriverHint));
    }


    public SensorsSettings Options => (SensorsSettings)Settings;

    public override string Title => "Sensors";

    public double CpuTempPercent { get => _cpuTempPercent; private set => SetProperty(ref _cpuTempPercent, value); }

    public string CpuTempText { get => _cpuTempText; private set => SetProperty(ref _cpuTempText, value); }

    public double GpuTempPercent { get => _gpuTempPercent; private set => SetProperty(ref _gpuTempPercent, value); }

    public string GpuTempText { get => _gpuTempText; private set => SetProperty(ref _gpuTempText, value); }

    public double FanPercent { get => _fanPercent; private set => SetProperty(ref _fanPercent, value); }

    public string FanText { get => _fanText; private set => SetProperty(ref _fanText, value); }

    public IReadOnlyList<SensorRow> Temperatures
    {
        get => _temperatures;
        private set
        {
            if (SetProperty(ref _temperatures, value))
            {
                OnPropertyChanged(nameof(HasTemperatures));
            }
        }
    }

    public IReadOnlyList<SensorRow> Fans
    {
        get => _fans;
        private set
        {
            if (SetProperty(ref _fans, value))
            {
                OnPropertyChanged(nameof(HasFans));
            }
        }
    }

    public bool HasTemperatures => Options.ShowTemperatures && _temperatures.Count > 0;

    public bool IsTempAlert { get => _isTempAlert; private set => SetProperty(ref _isTempAlert, value); }

    public string AlertText { get => _alertText; private set => SetProperty(ref _alertText, value); }

    public bool HasFans => Options.ShowFans && _fans.Count > 0;

    protected override void Update(MetricsSnapshot snapshot)
    {
        double? cpuTemp = snapshot.Cpu?.TemperatureC;
        double? gpuTemp = snapshot.Gpus is { Count: > 0 } gpus ? gpus[0].TemperatureC : null;

        CpuTempPercent = Math.Clamp(cpuTemp ?? 0, 0, 100);
        CpuTempText = cpuTemp is { } c ? FormatTemp(c, shortForm: true) : "—";
        GpuTempPercent = Math.Clamp(gpuTemp ?? 0, 0, 100);
        GpuTempText = gpuTemp is { } g ? FormatTemp(g, shortForm: true) : "—";

        ApplyDriverHint(snapshot.SensorHint);
        if (snapshot.Cpu?.ApproximateTemperature == true && cpuTemp is { } approx)
        {
            CpuTempText = "≈" + FormatTemp(approx, shortForm: true);
        }

        double hottest = Math.Max(cpuTemp ?? 0, gpuTemp ?? 0);
        IsTempAlert = hottest >= Options.TempAlertC;
        AlertText = IsTempAlert ? $"HOT {FormatTemp(hottest, shortForm: true)}" : string.Empty;

        IReadOnlyList<SensorReading> sensors = snapshot.Sensors ?? Array.Empty<SensorReading>();

        var fanReadings = sensors.Where(s => s.Kind == "fan").ToList();
        double maxFan = fanReadings.Count > 0 ? fanReadings.Max(s => s.Value) : 0;
        double fullScale = Math.Max(500, Options.FanFullScaleRpm);
        FanPercent = Math.Clamp(maxFan / fullScale * 100, 0, 100);
        FanText = maxFan >= 1
            ? (maxFan >= 1000
                ? (maxFan / 1000).ToString("0.0", CultureInfo.InvariantCulture) + "k"
                : maxFan.ToString("0", CultureInfo.InvariantCulture))
            : "OFF";

        int maxRows = Math.Clamp(Options.MaxTemperatureRows, 4, 30);
        Temperatures = sensors
            .Where(s => s.Kind == "temperature")
            .OrderByDescending(s => s.Value)
            .Take(maxRows)
            .Select(s => new SensorRow(Label(s), FormatTemp(s.Value, shortForm: false), Math.Clamp(s.Value, 0, 100)))
            .ToList();

        Fans = Options.ShowFans
            ? (IReadOnlyList<SensorRow>)fanReadings
                .Where(s => s.Value >= 1)
                .Select(s => new SensorRow(Label(s), UnitFormat.Rpm(s.Value), Math.Clamp(s.Value / fullScale * 100, 0, 100)))
                .ToList()
            : Array.Empty<SensorRow>();
    }

    private static string Label(SensorReading s)
        => s.Name.StartsWith(s.Group, StringComparison.OrdinalIgnoreCase) ? s.Name : $"{s.Group} · {s.Name}";

    private string FormatTemp(double celsius, bool shortForm)
    {
        double value = Options.UseFahrenheit ? celsius * 1.8 + 32 : celsius;
        return shortForm
            ? $"{value.ToString("0", CultureInfo.InvariantCulture)}°"
            : $"{value.ToString("0", CultureInfo.InvariantCulture)}°{(Options.UseFahrenheit ? "F" : "C")}";
    }
}
