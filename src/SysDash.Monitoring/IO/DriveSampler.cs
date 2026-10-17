using SysDash.Core.Diagnostics;

namespace SysDash.Monitoring.IO;

/// <summary>
/// Logical drive sampling. Capacity comes from System.IO.DriveInfo; IO rates come
/// from the LogicalDisk performance counters with graceful fallback to zero.
/// Adapted from SidebarDiagnostics' DriveMonitor (Monitoring.cs) — GPLv3, see NOTICE.
/// </summary>
public sealed class DriveSampler : IDisposable
{
    private const string Category = "LogicalDisk";
    private const string ReadRateCounter = "Disk Read Bytes/sec";
    private const string WriteRateCounter = "Disk Write Bytes/sec";

    private readonly Dictionary<string, System.Diagnostics.PerformanceCounter> _readCounters = new();
    private readonly Dictionary<string, System.Diagnostics.PerformanceCounter> _writeCounters = new();
    // Instances whose counters failed (e.g. a USB stick not yet in LogicalDisk) —
    // per drive, so one odd drive never disables I/O rates for all the others.
    private readonly HashSet<string> _failed = new(StringComparer.OrdinalIgnoreCase);

    public List<Core.Models.DriveReading> ReadAll()
    {
        var result = new List<Core.Models.DriveReading>();

        try
        {
            foreach (var drive in DriveInfo.GetDrives())
            {
                // Each drive on its own: an ejected / locked / access-denied drive
                // is skipped without hiding the drives after it.
                try
                {
                    if (drive.DriveType is not (DriveType.Fixed or DriveType.Removable) || !drive.IsReady)
                    {
                        continue;
                    }

                    double total = drive.TotalSize;
                    double free = drive.AvailableFreeSpace;
                    double used = Math.Max(0, total - free);
                    string instance = drive.Name.TrimEnd('\\');

                    double read = 0;
                    double write = 0;
                    if (!_failed.Contains(instance))
                    {
                        TryGetRates(instance, out read, out write);
                    }

                    result.Add(new Core.Models.DriveReading(drive.Name, total, used, free, read, write));
                }
                catch (Exception ex)
                {
                    Log.Debug($"Drive {drive.Name} skipped: {ex.Message}");
                }
            }
        }
        catch (Exception ex)
        {
            Log.Warn($"Drive sampling failed: {ex.Message}");
        }

        return result;
    }

    private void TryGetRates(string instance, out double read, out double write)
    {
        read = 0;
        write = 0;

        try
        {
            if (!_readCounters.TryGetValue(instance, out var readCounter))
            {
                readCounter = new System.Diagnostics.PerformanceCounter(Category, ReadRateCounter, instance, readOnly: true);
                _readCounters[instance] = readCounter;
                readCounter.NextValue(); // warm-up sample; rate computed on the second call
            }

            if (!_writeCounters.TryGetValue(instance, out var writeCounter))
            {
                writeCounter = new System.Diagnostics.PerformanceCounter(Category, WriteRateCounter, instance, readOnly: true);
                _writeCounters[instance] = writeCounter;
                writeCounter.NextValue();
            }

            read = Math.Max(0, readCounter.NextValue());
            write = Math.Max(0, writeCounter.NextValue());
        }
        catch
        {
            _failed.Add(instance);
            if (_readCounters.Remove(instance, out var r))
            {
                r.Dispose();
            }

            if (_writeCounters.Remove(instance, out var w))
            {
                w.Dispose();
            }
        }
    }

    private void DisposeCounters()
    {
        foreach (var c in _readCounters.Values)
        {
            c.Dispose();
        }
        foreach (var c in _writeCounters.Values)
        {
            c.Dispose();
        }
        _readCounters.Clear();
        _writeCounters.Clear();
    }

    public void Dispose() => DisposeCounters();
}