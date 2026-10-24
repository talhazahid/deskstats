using System.Globalization;
using SysDash.Core.Models;
using SysDash.Core.Threading;
using SysDash.SDK.Widgets;

namespace SysDash.Widgets.Power;

public sealed record PowerRow(string Device, string Name, string ValueText);

public sealed class PowerViewModel : WidgetViewModel
{
    public PowerViewModel(IThreadMarshaller marshaller, PowerSettings settings) : base(marshaller, settings) { }
    public override string Title => "Hardware power";
    public IReadOnlyList<PowerRow> Power { get; private set; } = Array.Empty<PowerRow>();
    public string PowerHint { get; private set; } = "Power readings are unavailable on this hardware.";
    public string BoardHint { get; private set; } = string.Empty;
    public string UpdatedText { get; private set; } = "Waiting for sensor readings…";

    protected override void Update(MetricsSnapshot snapshot)
    {
        var readings = (snapshot.Sensors ?? Array.Empty<SensorReading>())
            .Where(s => s.Kind == "power" && double.IsFinite(s.Value) && s.Value >= 0).ToList();
        if (snapshot.Cpu?.PackagePowerW is { } cpu && double.IsFinite(cpu) && cpu >= 0 &&
            !readings.Any(s => s.Group.StartsWith("CPU", StringComparison.OrdinalIgnoreCase) && s.Name.Contains("Package", StringComparison.OrdinalIgnoreCase)))
            readings.Insert(0, new("CPU", "Package", "power", cpu));
        Power = readings.OrderBy(s => s.Group.StartsWith("CPU", StringComparison.OrdinalIgnoreCase) ? 0 : s.Group.StartsWith("GPU", StringComparison.OrdinalIgnoreCase) ? 1 : 2)
            .ThenBy(s => s.Group).ThenBy(s => s.Name.Contains("Total board", StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .Select(s => new PowerRow(s.Group, s.Name, s.Value.ToString("0.0", CultureInfo.InvariantCulture) + " W")).ToArray();
        PowerHint = readings.Count == 0 ? "Power readings are unavailable on this hardware."
            : readings.Any(s => s.Group.StartsWith("PSU", StringComparison.OrdinalIgnoreCase))
                ? "Readings can overlap. PSU output is not wall-socket consumption."
                : "Total PC power unavailable: a supported PSU with telemetry is required. Component readings are not added together.";
        var gpuGroups = readings.Where(s => s.Group.StartsWith("GPU", StringComparison.OrdinalIgnoreCase)).GroupBy(s => s.Group);
        BoardHint = string.Join("\n", gpuGroups.Where(g => !g.Any(s => s.Name.Contains("Total board", StringComparison.OrdinalIgnoreCase)))
            .Select(g => $"{g.Key}: total board power unavailable. Chip/package readings cover a different scope."));
        if (!gpuGroups.Any() && snapshot.Gpus is { Count: > 0 })
            BoardHint = "GPU power unavailable: the graphics driver is not exposing a readable power sensor.";
        UpdatedText = "Live readings · " + snapshot.Timestamp.ToLocalTime().ToString("HH:mm:ss", CultureInfo.InvariantCulture);
        OnPropertyChanged(string.Empty);
    }
}
