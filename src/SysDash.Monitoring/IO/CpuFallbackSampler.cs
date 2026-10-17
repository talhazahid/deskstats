using System.Diagnostics;
using Microsoft.Win32;
using SysDash.Core.Diagnostics;
using SysDash.Core.Models;

namespace SysDash.Monitoring.IO;

/// <summary>
/// CPU readings that work on every Windows PC without a kernel driver, used when
/// LibreHardwareMonitor cannot read the CPU directly (PawnIO missing / not admin):
///   • effective clock  = "Processor Frequency" × "% Processor Performance" / 100
///     (Processor Information counters — what Task Manager shows as "Speed");
///   • temperature (approximate) = hottest ACPI "Thermal Zone Information" zone.
/// ACPI zones are motherboard firmware readings, so the value is flagged as
/// approximate in the UI.
/// </summary>
public sealed class CpuFallbackSampler : IDisposable
{
    private readonly List<PerformanceCounter> _threads = new();
    private PerformanceCounter? _total;
    private bool _threadsInitialized;
    private PerformanceCounter? _frequency;
    private PerformanceCounter? _performance;
    private readonly List<PerformanceCounter> _zones = new();
    private bool _clockUnavailable;
    private bool _tempUnavailable;
    private bool _initialized;

    /// <summary>
    /// A complete CPU reading from Windows alone — used when the sensor library
    /// cannot read this CPU at all (ARM laptops, brand-new chips, blocked drivers).
    /// </summary>
    public CpuReading ReadAll()
    {
        double? temp = ReadTemperatureC();
        return new CpuReading(
            CpuName(),
            ReadTotalLoad(),
            ReadClockMhz(),
            temp,
            null,
            null,
            ReadThreadLoads(),
            null,
            null,
            ApproximateTemperature: temp is not null);
    }

    /// <summary>Processor name from Windows (e.g. "AMD Ryzen 5 5600X 6-Core Processor").</summary>
    public static string CpuName()
    {
        try
        {
            using RegistryKey? key = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0");
            string? name = key?.GetValue("ProcessorNameString") as string;
            if (!string.IsNullOrWhiteSpace(name))
            {
                return name.Trim();
            }
        }
        catch
        {
            // fall through
        }

        return "Processor";
    }

    /// <summary>Total CPU load in % (what Task Manager shows), or null.</summary>
    public double? ReadTotalLoad()
    {
        EnsureThreads();
        try
        {
            return _total is null ? null : Math.Clamp(_total.NextValue(), 0, 100);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Load of every logical processor, in Windows order, for any CPU vendor and
    /// any number of processor groups (instances "group,index"), or null.
    /// </summary>
    public IReadOnlyList<double>? ReadThreadLoads()
    {
        EnsureThreads();
        if (_threads.Count == 0)
        {
            return null;
        }

        try
        {
            return _threads.Select(c => (double)Math.Clamp(c.NextValue(), 0, 100)).ToList();
        }
        catch
        {
            return null;
        }
    }

    private void EnsureThreads()
    {
        if (_threadsInitialized)
        {
            return;
        }

        _threadsInitialized = true;
        try
        {
            const string category = "Processor Information";
            _total = new PerformanceCounter(category, "% Processor Time", "_Total", readOnly: true);
            _total.NextValue();

            var instances = new PerformanceCounterCategory(category).GetInstanceNames()
                .Select(name => (name, parts: name.Split(',')))
                .Where(x => x.parts.Length == 2 &&
                            int.TryParse(x.parts[0], out _) && int.TryParse(x.parts[1], out _))
                .OrderBy(x => int.Parse(x.parts[0]))
                .ThenBy(x => int.Parse(x.parts[1]))
                .Select(x => x.name)
                .Take(Math.Max(1, Environment.ProcessorCount));

            foreach (string instance in instances)
            {
                var counter = new PerformanceCounter(category, "% Processor Time", instance, readOnly: true);
                counter.NextValue(); // prime
                _threads.Add(counter);
            }
        }
        catch (Exception ex)
        {
            Log.Debug($"Per-thread CPU counters unavailable: {ex.Message}");
        }
    }

    /// <summary>Effective average clock in MHz, or null when the counters are missing.</summary>
    public double? ReadClockMhz()
    {
        EnsureInitialized();
        if (_clockUnavailable || _frequency is null || _performance is null)
        {
            return null;
        }

        try
        {
            double baseMhz = _frequency.NextValue();
            double percent = _performance.NextValue();
            double mhz = baseMhz * percent / 100.0;
            return mhz > 100 ? (double?)mhz : null;
        }
        catch
        {
            _clockUnavailable = true;
            return null;
        }
    }

    /// <summary>Hottest ACPI thermal zone in °C, or null when unavailable / implausible.</summary>
    public double? ReadTemperatureC()
    {
        EnsureInitialized();
        if (_tempUnavailable || _zones.Count == 0)
        {
            return null;
        }

        try
        {
            double hottest = double.MinValue;
            foreach (PerformanceCounter zone in _zones)
            {
                // "High Precision Temperature" is tenths of a Kelvin; "Temperature" is Kelvin.
                double raw = zone.NextValue();
                double kelvin = zone.CounterName.StartsWith("High", StringComparison.OrdinalIgnoreCase) ? raw / 10.0 : raw;
                hottest = Math.Max(hottest, kelvin - 273.15);
            }

            return hottest is > 15 and < 115 ? (double?)hottest : null;
        }
        catch
        {
            _tempUnavailable = true;
            return null;
        }
    }

    private void EnsureInitialized()
    {
        if (_initialized)
        {
            return;
        }

        _initialized = true;

        try
        {
            _frequency = new PerformanceCounter("Processor Information", "Processor Frequency", "_Total", readOnly: true);
            _performance = new PerformanceCounter("Processor Information", "% Processor Performance", "_Total", readOnly: true);
            _frequency.NextValue();
            _performance.NextValue(); // first sample primes the rate counter
        }
        catch (Exception ex)
        {
            _clockUnavailable = true;
            Log.Debug($"CPU clock counters unavailable: {ex.Message}");
        }

        try
        {
            var category = new PerformanceCounterCategory("Thermal Zone Information");
            foreach (string instance in category.GetInstanceNames())
            {
                string counter = category.CounterExists("High Precision Temperature")
                    ? "High Precision Temperature"
                    : "Temperature";
                _zones.Add(new PerformanceCounter("Thermal Zone Information", counter, instance, readOnly: true));
            }

            _tempUnavailable = _zones.Count == 0;
        }
        catch (Exception ex)
        {
            _tempUnavailable = true;
            Log.Debug($"ACPI thermal zones unavailable: {ex.Message}");
        }
    }

    public void Dispose()
    {
        _total?.Dispose();
        foreach (PerformanceCounter thread in _threads)
        {
            thread.Dispose();
        }

        _threads.Clear();
        _frequency?.Dispose();
        _performance?.Dispose();
        foreach (PerformanceCounter zone in _zones)
        {
            zone.Dispose();
        }

        _zones.Clear();
    }
}
