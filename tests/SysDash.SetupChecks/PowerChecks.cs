using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows.Threading;
using SysDash.App;
using SysDash.Core.Models;
using SysDash.Widgets.Sensors;

internal static class PowerChecks
{
    public static bool Run(string output)
    {
        using var model = new SensorsViewModel(new ThreadMarshaller(Dispatcher.CurrentDispatcher), new SensorsSettings());
        var update = typeof(SensorsViewModel).GetMethod("Update", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var checks = new Dictionary<string, bool>();
        update.Invoke(model, new object[] { MetricsSnapshot.Empty });
        checks["MissingPowerIsNotZeroOrFakeTotal"] = model.Power.Count == 0 && model.PowerHint.Contains("unavailable");
        var sensors = new[] { new SensorReading("CPU", "Package", "power", 45), new SensorReading("CPU", "Core", "power", 20),
            new SensorReading("GPU", "GPU Package", "power", 80), new SensorReading("GPU", "Core", "power", 0),
            new SensorReading("GPU", "Invalid", "power", double.NaN), new SensorReading("GPU", "Invalid", "power", -2) };
        var cpu = new CpuReading("CPU", null, null, null, null, null, null, null, 45);
        update.Invoke(model, new object[] { new MetricsSnapshot(DateTimeOffset.Now, Cpu: cpu, Sensors: sensors) });
        checks["OverlappingSourcesRemainSeparate"] = model.Power.Count == 4;
        checks["PackageNotDuplicatedByFallback"] = model.Power.Count(r => r.Name.Contains("Package") && r.Name.Contains("CPU")) == 1;
        checks["ValidZeroWattReadingRetained"] = model.Power.Any(r => r.ValueText == "0.0 W");
        checks["UnsupportedPsuDisclosed"] = model.PowerHint.Contains("supported PSU");
        update.Invoke(model, new object[] { new MetricsSnapshot(DateTimeOffset.Now, Sensors: new[] { new SensorReading("PSU · Test", "Total Output", "power", 160) }) });
        checks["PsuOutputNotLabeledWallConsumption"] = model.Power.Count == 1 && model.PowerHint.Contains("not wall-socket");
        File.WriteAllText(Path.Combine(output, "power-checks.json"), JsonSerializer.Serialize(checks, new JsonSerializerOptions { WriteIndented = true }));
        return checks.Values.All(v => v);
    }
}
