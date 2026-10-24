using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows.Threading;
using SysDash.App;
using SysDash.Core.Models;
using SysDash.Widgets.Power;
using SysDash.Monitoring.Hardware;

internal static class PowerChecks
{
    public static bool Run(string output)
    {
        using var model = new PowerViewModel(new ThreadMarshaller(Dispatcher.CurrentDispatcher), new PowerSettings());
        var update = typeof(PowerViewModel).GetMethod("Update", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var checks = new Dictionary<string, bool>();
        update.Invoke(model, new object[] { MetricsSnapshot.Empty });
        checks["MissingPowerIsNotZeroOrFakeTotal"] = model.Power.Count == 0 && model.PowerHint.Contains("unavailable");
        var sensors = new[] { new SensorReading("CPU", "Package", "power", 45), new SensorReading("CPU", "Core", "power", 20),
            new SensorReading("GPU", "GPU Package", "power", 80), new SensorReading("GPU", "Core", "power", 0),
            new SensorReading("GPU", "Invalid", "power", double.NaN), new SensorReading("GPU", "Invalid", "power", -2) };
        var cpu = new CpuReading("CPU", null, null, null, null, null, null, null, 45);
        update.Invoke(model, new object[] { new MetricsSnapshot(DateTimeOffset.Now, Cpu: cpu, Sensors: sensors) });
        checks["OverlappingSourcesRemainSeparate"] = model.Power.Count == 4;
        checks["PackageNotDuplicatedByFallback"] = model.Power.Count(r => r.Name.Contains("Package") && r.Device.Contains("CPU")) == 1;
        checks["ValidZeroWattReadingRetained"] = model.Power.Any(r => r.ValueText == "0.0 W");
        checks["UnsupportedPsuDisclosed"] = model.PowerHint.Contains("supported PSU");
        update.Invoke(model, new object[] { new MetricsSnapshot(DateTimeOffset.Now, Sensors: new[] { new SensorReading("PSU · Test", "Total Output", "power", 160) }) });
        checks["PsuOutputNotLabeledWallConsumption"] = model.Power.Count == 1 && model.PowerHint.Contains("not wall-socket");
        var direct = AmdPowerTelemetry.Decode("RX Test", true, 29, true, 151);
        update.Invoke(model, new object[] { new MetricsSnapshot(DateTimeOffset.Now, Sensors: direct) });
        checks["BoardAndChipRemainSeparate"] = model.Power.Any(r => r.Name == "Total board power" && r.ValueText == "151.0 W") && model.Power.Any(r => r.Name == "ASIC / chip power" && r.ValueText == "29.0 W");
        checks["SupportedBoardHasNoUnavailableWarning"] = model.BoardHint.Length == 0;
        var chipOnly = AmdPowerTelemetry.Decode("RX Test", true, 29, false, 151);
        update.Invoke(model, new object[] { new MetricsSnapshot(DateTimeOffset.Now, Sensors: chipOnly) });
        checks["UnsupportedBoardCannotUseItsValueOrFakeTotal"] = model.Power.Count == 1 && model.Power[0].ValueText == "29.0 W" && model.BoardHint.Contains("total board power unavailable");
        checks["InvalidNativeValuesRejected"] = AmdPowerTelemetry.Decode("Test", true, double.NaN, true, -1).Count == 0;
        checks["IndependentWidgetRegistered"] = new WidgetRegistry().Find("power") is PowerWidget;
        checks["SensorWidgetHasNoPowerBranch"] = typeof(SysDash.Widgets.Sensors.SensorsViewModel).GetProperty("Power") is null;
        update.Invoke(model, new object[] { MetricsSnapshot.Empty });
        checks["UnavailableSnapshotClearsOldWatts"] = model.Power.Count == 0;
        File.WriteAllText(Path.Combine(output, "power-checks.json"), JsonSerializer.Serialize(checks, new JsonSerializerOptions { WriteIndented = true }));
        return checks.Values.All(v => v);
    }
}
