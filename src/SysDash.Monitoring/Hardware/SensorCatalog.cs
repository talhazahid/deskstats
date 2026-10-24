using System.Text.RegularExpressions;
using LibreHardwareMonitor.Hardware;
using SysDash.Core.Models;

namespace SysDash.Monitoring.Hardware;

/// <summary>
/// Sensor-selection heuristics for CPU / RAM / GPU. Adapted from SidebarDiagnostics'
/// Monitoring.cs (OHMMonitor.InitCPU / InitRAM / InitGPU) — GPLv3, see NOTICE.
///
/// All methods tolerate missing sensors: an unavailable reading is simply null
/// so widgets can fall back visually instead of failing.
/// </summary>
public static class SensorCatalog
{
    private static readonly Regex CoreClockRegex = new(@"^.*(CPU|Core).*#(\d+)$", RegexOptions.Compiled);

    /// <summary>
    /// A per-core / per-thread sensor on any vendor: "CPU Core #3", "Core #3",
    /// "CPU Core #1 Thread #2", Intel hybrid "P-Core #2" / "E-Core #5".
    /// </summary>
    private static readonly Regex PerCoreRegex = new(@"#\d+", RegexOptions.Compiled);

    /// <summary>Aggregate sensors that are NOT a core/thread of their own.</summary>
    private static bool IsAggregate(string name)
        => name.Contains("Max", StringComparison.OrdinalIgnoreCase) ||
           name.Contains("Total", StringComparison.OrdinalIgnoreCase) ||
           name.Contains("Average", StringComparison.OrdinalIgnoreCase);

    /// <summary>Intel's "Distance to TjMax" sensors are typed as temperatures but are not temperatures.</summary>
    private static bool IsRealTemperature(ISensor s)
        => s.SensorType == SensorType.Temperature &&
           !s.Name.Contains("Distance", StringComparison.OrdinalIgnoreCase) &&
           Value(s) is > 0 and < 150;

    public static double? Value(ISensor? sensor)
        => sensor?.Value is float v && !float.IsNaN(v) && !float.IsInfinity(v) ? v : null;

    public static CpuReading? ReadCpu(IHardware cpu, IHardware? board)
    {
        try
        {
            ISensor[] sensors = cpu.Sensors;
            string name = cpu.Name;

            // Total load: the "CPU Total" sensor on every vendor (index 0 as a fallback).
            double? totalLoad = Value(
                sensors.FirstOrDefault(s => s.SensorType == SensorType.Load && s.Name.Contains("Total", StringComparison.OrdinalIgnoreCase))
                ?? sensors.FirstOrDefault(s => s.SensorType == SensorType.Load && s.Index == 0));

            // Per-thread loads, identified by NAME ("… #n"), never by position:
            // aggregates such as "CPU Core Max" are excluded (they showed up as an
            // extra thread, e.g. 13 on a 12-thread CPU). Never more entries than
            // Windows reports logical processors.
            var coreLoads = sensors
                .Where(s => s.SensorType == SensorType.Load && PerCoreRegex.IsMatch(s.Name) && !IsAggregate(s.Name))
                .OrderBy(s => s.Index)
                .Select(Value)
                .Where(v => v.HasValue)
                .Select(v => v!.Value)
                .Take(Environment.ProcessorCount)
                .ToList();

            // Temperature — the chip's own package reading first, for every vendor:
            //   Intel "CPU Package"; AMD Ryzen "Core (Tctl/Tdie)" / "Tdie" / "Tctl";
            //   AMD chiplets "CCDs Max (Tdie)" / "CCD1 (Tdie)"; then "Core Max" /
            //   "Core Average"; then any real CPU temperature; then the motherboard's
            //   "CPU" sensor (Super I/O, least exact). "Distance to TjMax" is skipped.
            string[] preferred = { "CPU Package", "Package", "Core (Tctl/Tdie)", "Tdie", "Tctl", "CCDs Max", "CCD1", "Core Max", "Core Average" };
            ISensor? temp = preferred
                    .Select(key => sensors.FirstOrDefault(s => IsRealTemperature(s) && s.Name.Contains(key, StringComparison.OrdinalIgnoreCase)))
                    .FirstOrDefault(s => s is not null)
                ?? sensors.FirstOrDefault(IsRealTemperature)
                ?? BoardSensors(board).FirstOrDefault(s =>
                    IsRealTemperature(s) && s.Name.Contains("CPU", StringComparison.OrdinalIgnoreCase));

            // Clock: named "CPU Core #n" / "Core #n".
            var clock = sensors
                .Where(s => s.SensorType == SensorType.Clock)
                .Select(s => new { Match = CoreClockRegex.Match(s.Name), Sensor = s })
                .Where(x => x.Match.Success)
                .OrderBy(x => int.Parse(x.Match.Groups[2].Value))
                .ToList();

            var clocks = clock.Select(x => Value(x.Sensor)).Where(v => v.HasValue).Select(v => v!.Value).ToList();
            double? totalClock = clocks.Count > 0 ? clocks.Average() : null;

            // Voltage: the CPU's own core voltage (Ryzen "Core (SVI2 TFN)", Intel
            // "CPU Core"), else the board's "CPU" voltage (Super I/O, less exact).
            ISensor? voltage = sensors.FirstOrDefault(s =>
                    s.SensorType == SensorType.Voltage &&
                    s.Name.Contains("Core", StringComparison.OrdinalIgnoreCase) &&
                    Value(s) is > 0)
                ?? sensors.FirstOrDefault(s => s.SensorType == SensorType.Voltage && Value(s) is > 0)
                ?? BoardSensors(board).FirstOrDefault(s =>
                    s.SensorType == SensorType.Voltage &&
                    s.Name.Contains("CPU", StringComparison.OrdinalIgnoreCase));

            // Fan: only a fan that is really the CPU's (board "CPU Fan", "CPU OPT",
            // AIO "Pump"), in RPM. No guessing: a case fan must never be shown as
            // the CPU fan, and laptops simply have no row.
            ISensor? fan = BoardSensors(board).Concat(sensors).FirstOrDefault(s =>
                    s.SensorType == SensorType.Fan && Value(s) is > 0 &&
                    (s.Name.Contains("CPU", StringComparison.OrdinalIgnoreCase) ||
                     s.Name.Contains("Pump", StringComparison.OrdinalIgnoreCase)));

            double? packagePower = Value(sensors.FirstOrDefault(s =>
                    s.SensorType == SensorType.Power &&
                    s.Name.Contains("Package", StringComparison.OrdinalIgnoreCase)));

            // A 0 reading means "sensor not accessible" (no admin / driver blocked).
            static double? Positive(double? v) => v is > 0 ? v : null;

            return new CpuReading(
                name,
                totalLoad,
                totalClock,
                Positive(Value(temp)),
                Positive(Value(voltage)),
                Positive(Value(fan)),
                coreLoads.Count > 0 ? coreLoads : null,
                clocks.Count > 0 ? clocks : null,
                Positive(packagePower));
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Sensors of the motherboard node AND its sub-hardware (Super I/O chip), where fans / temps live.</summary>
    private static IEnumerable<ISensor> BoardSensors(IHardware? board)
    {
        if (board is null)
        {
            return Array.Empty<ISensor>();
        }

        return board.Sensors.Concat(board.SubHardware.SelectMany(h => h.Sensors));
    }

    public static RamReading? ReadRam(IHardware memory)
    {
        try
        {
            ISensor[] sensors = memory.Sensors;

            // LibreHardwareMonitor reports memory "Data" sensors in GIGABYTES
            // (index 0 = used, 1 = available, 2 = virtual used, 3 = virtual
            // available). Convert to bytes — the UI formats bytes (this was the
            // "17 B / 24 B" bug).
            const double GiB = 1024d * 1024 * 1024;

            double? load = Value(sensors.FirstOrDefault(s => s.SensorType == SensorType.Load && s.Index == 0));
            double? used = Value(sensors.FirstOrDefault(s => s.SensorType == SensorType.Data && s.Index == 0)) * GiB;
            double? free = Value(sensors.FirstOrDefault(s => s.SensorType == SensorType.Data && s.Index == 1)) * GiB;
            double? vUsed = Value(sensors.FirstOrDefault(s => s.SensorType == SensorType.Data && s.Index == 2)) * GiB;
            double? vFree = Value(sensors.FirstOrDefault(s => s.SensorType == SensorType.Data && s.Index == 3)) * GiB;

            double? total = (used.HasValue || free.HasValue) ? (used ?? 0) + (free ?? 0) : null;
            double? vTotal = (vUsed.HasValue || vFree.HasValue) ? (vUsed ?? 0) + (vFree ?? 0) : null;
            return new RamReading(used, total, load, free, vUsed, vTotal);
        }
        catch
        {
            return null;
        }
    }

    public static GpuReading? ReadGpu(IHardware gpu)
    {
        try
        {
            ISensor[] sensors = gpu.Sensors;

            double? coreLoad = Value(sensors.FirstOrDefault(s =>
                    s.SensorType == SensorType.Load && s.Name.Contains("Core", StringComparison.OrdinalIgnoreCase))
                ?? sensors.FirstOrDefault(s => s.SensorType == SensorType.Load && s.Index == 0));

            double? coreClock = Value(sensors.FirstOrDefault(s =>
                s.SensorType == SensorType.Clock && s.Name.Contains("Core", StringComparison.OrdinalIgnoreCase)));

            double? vramClock = Value(sensors.FirstOrDefault(s =>
                s.SensorType == SensorType.Clock && s.Name.Contains("Memory", StringComparison.OrdinalIgnoreCase)));

            // VRAM load: prefer GPU Memory Used / Total data sensors, else Load sensors.
            // Video memory, first pair that exists with a real total:
            //   NVIDIA / AMD "GPU Memory Used/Total" → Windows "D3D Dedicated Memory" →
            //   integrated GPUs (no own VRAM) "D3D Shared Memory".
            (ISensor? memUsed, ISensor? memTotal) = MemoryPair(sensors, "GPU Memory")
                ?? MemoryPair(sensors, "D3D Dedicated Memory")
                ?? MemoryPair(sensors, "D3D Shared Memory")
                ?? (null, null);

            double? vramLoad = null;
            if (memUsed is not null && memTotal is not null &&
                Value(memTotal) is > 0 && Value(memUsed) is { } usedBytes)
            {
                vramLoad = usedBytes / Value(memTotal)!.Value * 100;
            }
            else
            {
                // Only the exact "GPU Memory" load (never "Memory Controller" / D3D engines).
                vramLoad = Value(sensors.FirstOrDefault(s =>
                    s.SensorType == SensorType.Load && s.Name.Equals("GPU Memory", StringComparison.OrdinalIgnoreCase)));
            }

            // "GPU Core" on NVIDIA / AMD / Intel Arc; otherwise the first real reading.
            double? temp = Value(
                sensors.FirstOrDefault(s => IsRealTemperature(s) && s.Name.Contains("Core", StringComparison.OrdinalIgnoreCase))
                ?? sensors.FirstOrDefault(IsRealTemperature));
            double? voltage = Value(sensors.FirstOrDefault(s => s.SensorType == SensorType.Voltage && s.Index == 0));
            double? fan = Value(sensors
                .Where(s => s.SensorType == SensorType.Control)
                .OrderBy(s => s.Index)
                .FirstOrDefault());

            return new GpuReading(
                gpu.Name,
                coreLoad,
                vramLoad,
                coreClock,
                vramClock,
                temp,
                voltage,
                fan,
                // GPU memory sensors are reported in MEGABYTES.
                Value(memUsed) * 1024d * 1024,
                Value(memTotal) * 1024d * 1024);
        }
        catch
        {
            return null;
        }
    }

    private static (ISensor? Used, ISensor? Total)? MemoryPair(ISensor[] sensors, string prefix)
    {
        ISensor? Find(string suffix) => sensors.FirstOrDefault(s =>
            (s.SensorType is SensorType.Data or SensorType.SmallData) &&
            s.Name.Equals($"{prefix} {suffix}", StringComparison.OrdinalIgnoreCase));

        ISensor? used = Find("Used");
        ISensor? total = Find("Total");
        return used is not null && Value(total) is > 0 ? (used, total) : null;
    }

    /// <summary>
    /// Discrete graphics first (NVIDIA, AMD Radeon RX/Pro, Intel Arc A/B-series),
    /// integrated graphics last — so on laptops and desktops with an iGPU the
    /// GPU widget shows the real graphics card by default, for any vendor.
    /// </summary>
    public static int GpuRank(IHardware gpu)
    {
        string name = gpu.Name ?? string.Empty;
        return gpu.HardwareType switch
        {
            HardwareType.GpuNvidia => 0,
            HardwareType.GpuAmd when IsIntegratedAmd(name) => 2,
            HardwareType.GpuAmd => 0,
            HardwareType.GpuIntel when Regex.IsMatch(name, @"Arc(\(TM\))?\s*(Pro\s*)?[AB]\d{2,3}", RegexOptions.IgnoreCase) => 1,
            _ => 3,
        };
    }

    // "AMD Radeon(TM) Graphics", "Radeon 780M Graphics" = integrated; "Radeon RX 6600", "Radeon Pro W7600" = discrete.
    private static bool IsIntegratedAmd(string name)
        => name.Contains("Graphics", StringComparison.OrdinalIgnoreCase) &&
           !Regex.IsMatch(name, @"\bRX\b|\bPro\b|Vega\s*(56|64)|Radeon\s*VII", RegexOptions.IgnoreCase);

    /// <summary>
    /// Collects every temperature, fan and power sensor of the given hardware
    /// (including sub-hardware such as the motherboard Super I/O chip) for the
    /// Sensors widget. CPU first, then GPUs, then the rest.
    /// </summary>
    public static List<SensorReading> ReadSensors(IEnumerable<IHardware> hardware, AmdPowerTelemetry? amdPower = null)
    {
        var result = new List<SensorReading>();

        try
        {
            foreach (IHardware node in hardware.OrderBy(Rank))
            {
                AddSensors(result, node, GroupName(node));
                if (node.HardwareType == HardwareType.GpuAmd)
                {
                    string group = "GPU · " + node.Name;
                    var direct = amdPower?.Read(node) ?? Array.Empty<SensorReading>();
                    bool hasAsic = direct.Any(s => s.Name == "ASIC / chip power");
                    // LHM uses one Package slot for ASIC or board power depending on
                    // GPU generation. Do not silently label that slot as total board power.
                    for (int i = result.Count - 1; i >= 0; i--)
                    {
                        var s = result[i];
                        if (s.Group != group || s.Kind != "power" || s.Name != "GPU Package") continue;
                        if (hasAsic || direct.Any(s => s.Name == "Total board power")) result.RemoveAt(i);
                        else result[i] = s with { Name = "Package power (scope unverified)" };
                    }
                    result.AddRange(direct);
                }
                foreach (IHardware sub in node.SubHardware)
                {
                    AddSensors(result, sub, GroupName(node));
                }
            }
        }
        catch
        {
            // Sensor enumeration is best-effort.
        }

        return result;
    }

    private static void AddSensors(List<SensorReading> result, IHardware hardware, string group)
    {
        foreach (ISensor sensor in hardware.Sensors)
        {
            string? kind = sensor.SensorType switch
            {
                SensorType.Temperature => "temperature",
                SensorType.Fan => "fan",
                SensorType.Power => "power",
                _ => null,
            };

            if (kind is null || Value(sensor) is not { } value)
            {
                continue;
            }

            // Skip obviously bogus readings (disconnected headers report 0 / -128 / 255)
            // and Intel's "Distance to TjMax" values, which are not temperatures.
            if (kind == "temperature" && !IsRealTemperature(sensor))
            {
                continue;
            }

            if (kind == "fan" && value <= 0)
            {
                continue; // empty fan header
            }

            string sensorGroup = kind == "power" && hardware.HardwareType is HardwareType.GpuAmd or HardwareType.GpuNvidia or HardwareType.GpuIntel
                ? "GPU · " + hardware.Name : group;
            result.Add(new SensorReading(sensorGroup, sensor.Name, kind, value));
        }
    }

    private static int Rank(IHardware h) => h.HardwareType switch
    {
        HardwareType.Cpu => 0,
        HardwareType.GpuNvidia or HardwareType.GpuAmd or HardwareType.GpuIntel => 1,
        HardwareType.Motherboard => 2,
        _ => 3,
    };

    private static string GroupName(IHardware h) => h.HardwareType switch
    {
        HardwareType.Cpu => "CPU",
        HardwareType.GpuNvidia or HardwareType.GpuAmd or HardwareType.GpuIntel => "GPU",
        HardwareType.Motherboard => "Board",
        HardwareType.Memory => "Memory",
        HardwareType.Psu => "PSU · " + h.Name,
        _ => h.HardwareType.ToString(),
    };
}
