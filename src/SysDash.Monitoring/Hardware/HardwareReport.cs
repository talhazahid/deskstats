using System.Runtime.InteropServices;
using System.Text;
using LibreHardwareMonitor.Hardware;
using SysDash.Core.Diagnostics;
using SysDash.Core.Models;

namespace SysDash.Monitoring.Hardware;

/// <summary>
/// Writes %LocalAppData%\DeskStats\hardware-report.txt at start-up: Windows facts
/// (OS, architecture, logical processors), every hardware node and sensor the
/// sensor library found (type, index, name, value), and what DeskStats picked
/// from them (CPU temperature sensor, thread count, GPU order). Lets anyone verify
/// detection on any PC — Intel, AMD, ARM, laptops — and send one file for support.
/// No personal data: hardware names and sensor values only.
/// </summary>
public static class HardwareReport
{
    public static string FilePath => Path.Combine(AppPaths.DataDirectory, "hardware-report.txt");

    public static void Write(IComputer computer, IHardware? cpu, IHardware? board, IReadOnlyList<IHardware> gpus)
    {
        try
        {
            var sb = new StringBuilder();
            sb.AppendLine($"DeskStats hardware report — {DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss zzz}");
            sb.AppendLine($"Windows: {RuntimeInformation.OSDescription} ({RuntimeInformation.OSArchitecture})");
            sb.AppendLine($"Logical processors (Windows): {Environment.ProcessorCount}");
            sb.AppendLine($"Elevated: {SensorDriver.IsElevated}   PawnIO installed: {SensorDriver.IsPawnIOInstalled}");
            sb.AppendLine();

            sb.AppendLine("== What DeskStats uses ==");
            if (cpu is not null && SensorCatalog.ReadCpu(cpu, board) is { } reading)
            {
                sb.AppendLine($"CPU: {reading.Name}");
                sb.AppendLine($"  threads from sensors: {reading.PerCoreLoad?.Count ?? 0} (Windows: {Environment.ProcessorCount})");
                sb.AppendLine($"  temperature: {Fmt(reading.TemperatureC)} °C   clock: {Fmt(reading.ClockMhz)} MHz   power: {Fmt(reading.PackagePowerW)} W   fan: {Fmt(reading.FanRpm)} RPM");
            }
            else
            {
                sb.AppendLine("CPU: not readable by the sensor library — Windows fallback is used.");
            }

            for (int i = 0; i < gpus.Count; i++)
            {
                sb.AppendLine($"GPU #{i + 1} (rank {SensorCatalog.GpuRank(gpus[i])}): {gpus[i].Name}");
            }

            sb.AppendLine();
            sb.AppendLine("== All sensors ==");
            foreach (IHardware hardware in computer.Hardware)
            {
                AppendHardware(sb, hardware, indent: "");
            }

            Directory.CreateDirectory(AppPaths.DataDirectory);
            File.WriteAllText(FilePath, sb.ToString(), Encoding.UTF8);
            Log.Info($"Hardware report written: {FilePath}");
        }
        catch (Exception ex)
        {
            Log.Warn($"Hardware report failed: {ex.Message}");
        }
    }

    private static void AppendHardware(StringBuilder sb, IHardware hardware, string indent)
    {
        sb.AppendLine($"{indent}[{hardware.HardwareType}] {hardware.Name}");
        foreach (ISensor sensor in hardware.Sensors.OrderBy(s => s.SensorType).ThenBy(s => s.Index))
        {
            sb.AppendLine($"{indent}  {sensor.SensorType,-12} #{sensor.Index,-3} {sensor.Name,-40} {Fmt(SensorCatalog.Value(sensor))}");
        }

        foreach (IHardware sub in hardware.SubHardware)
        {
            AppendHardware(sb, sub, indent + "  ");
        }
    }

    private static string Fmt(double? value)
        => value is { } v ? v.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) : "—";
}
