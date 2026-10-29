using System.Diagnostics;
using SysDash.Core.Diagnostics;
using SysDash.Core.Models;
using SysDash.Monitoring.Hardware;
using SysDash.Monitoring.IO;
using LibreHardwareMonitor.Hardware;

namespace SysDash.Monitoring;

/// <summary>
/// Background hardware collector. Owns the LibreHardwareMonitor Computer instance,
/// samples drives + network, and publishes immutable MetricsSnapshots. All LHM
/// access happens on the timer thread; the UI only ever sees value snapshots.
/// </summary>
public sealed class MetricsService : IMetricsService
{
    private readonly MetricsOptions _options;
    private readonly object _gate = new();
    private readonly Stopwatch _watch = new();

    private Computer? _computer;
    private IHardware? _cpuHardware;
    private IHardware? _ramHardware;
    private IReadOnlyList<IHardware> _gpuHardware = Array.Empty<IHardware>();
    private IHardware? _board;
    private DriveSampler? _driveSampler;
    private NetworkSampler? _networkSampler;
    private readonly CpuFallbackSampler _cpuFallback = new();
    private readonly AmdPowerTelemetry _amdPower = new();
    private IHardware[] _cpuNodes = Array.Empty<IHardware>();

    // ARM PCs (Snapdragon): the PawnIO driver cannot help there, so never suggest it.
    private readonly bool _sensorsUnsupported =
        System.Runtime.InteropServices.RuntimeInformation.OSArchitecture == System.Runtime.InteropServices.Architecture.Arm64;
    private string? _sensorHint;
    private Timer? _timer;
    private bool _running;
    private bool _disposed;
    private readonly Task _initTask;
    private int _ticking;
    private bool _firstTickTraced;
    private bool _firstSnapshotTraced;
    private string? _lastHardwareWarning;
    private long _hardwareWarningTimestamp;
    private int _hardwareWarningRepeats;
    private MetricsSnapshot _latest = MetricsSnapshot.Empty;

    public MetricsService(MetricsOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));

        // Opening LibreHardwareMonitor takes ~0.7-1.5 s (driver + sensor scan).
        // It runs on a worker thread so the UI is created and shown meanwhile;
        // ticks are skipped until it has finished.
        _initTask = Task.Run(InitializeHardware);
    }

    public event Action<MetricsSnapshot>? SnapshotReady;

    public MetricsSnapshot Latest
    {
        get
        {
            lock (_gate)
            {
                return _latest;
            }
        }
    }

    public bool IsRunning => _running;

    public TimeSpan PollInterval => _options.PollInterval;

    public void Start()
    {
        if (_running || _disposed)
        {
            return;
        }

        _running = true;
        _timer = new Timer(OnTick, null, TimeSpan.FromMilliseconds(150), _options.PollInterval);

        // Publish the first snapshot as soon as hardware init finishes instead of
        // waiting for the next timer period.
        _ = _initTask.ContinueWith(_ => OnTick(null), TaskScheduler.Default);
        Log.Info($"MetricsService started (interval {_options.PollIntervalMs} ms)");
    }

    public void Stop()
    {
        _running = false;

        _timer?.Dispose();
        _timer = null;
    }

    public void ChangePollInterval(int milliseconds)
    {
        _options.PollIntervalMs = Math.Clamp(milliseconds, 200, 60_000);

        if (_running && _timer is not null)
        {
            _timer.Change(_options.PollInterval, _options.PollInterval);
        }
    }

    private void InitializeHardware()
    {
        var watch = Stopwatch.StartNew();
        SensorDriver.LogStatus();
        _sensorHint = SensorDriver.MissingReason;
        _driveSampler = new DriveSampler();
        _networkSampler = new NetworkSampler();

        try
        {
            var computer = new Computer
            {
                IsCpuEnabled = true,
                IsGpuEnabled = true,
                IsMemoryEnabled = true,
                IsControllerEnabled = true,
                IsMotherboardEnabled = true,
                IsPsuEnabled = true,
                IsStorageEnabled = false,
                IsNetworkEnabled = false,
            };
            computer.Open();

            // Warm-up update so the first published snapshot has real values.
            try
            {
                foreach (var node in computer.Hardware)
                {
                    try
                    {
                        node.Update();
                        foreach (var sub in node.SubHardware)
                        {
                            sub.Update();
                        }
                    }
                    catch (Exception ex)
                    {
                        Log.Warn($"Initial {node.HardwareType} update failed: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Warn($"Initial hardware update failed: {ex.Message}");
            }

            _computer = computer;
            _cpuNodes = computer.Hardware.Where(h => h.HardwareType == HardwareType.Cpu).ToArray();
            _cpuHardware = _cpuNodes.FirstOrDefault();
            _ramHardware = computer.Hardware.FirstOrDefault(h => h.HardwareType == HardwareType.Memory);
            _gpuHardware = computer.Hardware
                .Where(h => h.HardwareType is HardwareType.GpuNvidia or HardwareType.GpuAmd or HardwareType.GpuIntel)
                .OrderBy(SensorCatalog.GpuRank) // discrete first, integrated last (stable for equal ranks)
                .ToArray();
            _board = computer.Hardware.FirstOrDefault(h => h.HardwareType == HardwareType.Motherboard);

            Log.Info($"LibreHardwareMonitor ready: CPU={_cpuHardware is not null} RAM={_ramHardware is not null} GPUx{_gpuHardware.Count} board={_board is not null}");

            // Every PC is different: write what was detected and what DeskStats
            // chose, so any machine can be checked from one text file.
            HardwareReport.Write(computer, _cpuHardware, _board, _gpuHardware);
        }
        catch (Exception ex)
        {
            Log.Error("LibreHardwareMonitor initialization failed — hardware widgets will show fallback states", ex);
        }

        StartupTrace.Mark($"LHM hardware init finished ({watch.ElapsedMilliseconds} ms)");
    }

    private void OnTick(object? state)
    {
        if (!_running || _disposed || !_initTask.IsCompleted)
        {
            return;
        }

        // System.Threading.Timer fires even while the previous callback is still
        // running; overlapping samples would hit LHM / the samplers concurrently.
        if (Interlocked.Exchange(ref _ticking, 1) == 1)
        {
            return;
        }

        try
        {
            RunTick();
        }
        finally
        {
            Volatile.Write(ref _ticking, 0);
        }
    }

    private void RunTick()
    {
        if (!_firstTickTraced)
        {
            _firstTickTraced = true;
            StartupTrace.Mark("sampler first tick began");
        }

        _watch.Restart();

        MetricsSnapshot snapshot;
        try
        {
            snapshot = BuildSnapshot();
        }
        catch (Exception ex)
        {
            Log.Error("Snapshot build failed", ex);
            return;
        }

        lock (_gate)
        {
            _latest = snapshot;
        }

        long elapsed = _watch.ElapsedMilliseconds;
        if (elapsed > 1500)
        {
            Log.Warn($"Sampling took {elapsed} ms (slow machine or busy sensor)");
        }

        if (!_firstSnapshotTraced)
        {
            _firstSnapshotTraced = true;
            StartupTrace.Mark("first snapshot built");
        }

        try
        {
            SnapshotReady?.Invoke(snapshot);
        }
        catch (Exception ex)
        {
            Log.Error("Snapshot subscriber threw", ex);
        }
    }

    private void LogHardwareWarning(string message)
    {
        bool same = string.Equals(message, _lastHardwareWarning, StringComparison.Ordinal);
        if (same)
        {
            _hardwareWarningRepeats++;
            if (Stopwatch.GetElapsedTime(_hardwareWarningTimestamp) < TimeSpan.FromMinutes(5)) return;
        }
        if (_hardwareWarningRepeats > 0)
            Log.Warn($"Hardware read failed: {_lastHardwareWarning} (repeated {_hardwareWarningRepeats} times)");
        if (!same) Log.Warn($"Hardware read failed: {message}");
        _lastHardwareWarning = message;
        _hardwareWarningTimestamp = Stopwatch.GetTimestamp();
        _hardwareWarningRepeats = 0;
    }

    private MetricsSnapshot BuildSnapshot()
    {
        CpuReading? cpu = null;
        RamReading? ram = null;
        var gpus = _options.IncludeGpu ? new List<GpuReading>() : null;
        List<SensorReading>? sensors = null;

        Computer? computer = _computer;
        if (computer is not null && !_disposed)
        {
            try
            {
                // Update every hardware node (including board for CPU temp/voltage).
                foreach (var node in computer.Hardware)
                {
                    try
                    {
                        node.Update();
                        foreach (var sub in node.SubHardware)
                        {
                            sub.Update(); // board fans / temps live on the Super I/O sub-hardware
                        }
                    }
                    catch
                    {
                        // One broken node must not kill the whole snapshot.
                    }
                }

                if (_cpuHardware is not null)
                {
                    cpu = SensorCatalog.ReadCpu(_cpuHardware, _board);

                    // Multi-socket (one LibreHardwareMonitor node per socket): the
                    // whole system's load/threads come from Windows below; take the
                    // hottest socket and the summed package power.
                    if (cpu is not null && _cpuNodes.Length > 1)
                    {
                        var all = _cpuNodes.Select(n => SensorCatalog.ReadCpu(n, _board)).Where(r => r is not null).ToList();
                        double? hottest = all.Max(r => r!.TemperatureC);
                        double power = all.Sum(r => r!.PackagePowerW ?? 0);
                        cpu = cpu with
                        {
                            TemperatureC = hottest ?? cpu.TemperatureC,
                            PackagePowerW = power > 0 ? power : cpu.PackagePowerW,
                            PerCoreLoad = null, // refilled for ALL sockets from Windows
                            Load = null,
                        };
                    }
                }

                if (_ramHardware is not null)
                {
                    ram = SensorCatalog.ReadRam(_ramHardware);
                }

                sensors = SensorCatalog.ReadSensors(computer.Hardware, _amdPower);

                if (gpus is not null)
                {
                    foreach (var gpu in _gpuHardware)
                    {
                        var reading = SensorCatalog.ReadGpu(gpu);
                        if (reading is not null)
                        {
                            gpus.Add(reading);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                LogHardwareWarning(ex.Message);
            }
        }

        // No direct CPU access (PawnIO missing / not elevated): fill the gaps with
        // driver-free Windows counters so the CPU widget is never blank.
        // CPU the sensor library cannot read at all (e.g. ARM laptops, unsupported
        // or very new chips): build the reading from Windows itself.
        if (cpu is null)
        {
            cpu = _cpuFallback.ReadAll();
        }
        else if (cpu is not null && (cpu.PerCoreLoad is null || cpu.PerCoreLoad.Count == 0 || cpu.Load is null))
        {
            IReadOnlyList<double>? threads = _cpuFallback.ReadThreadLoads();
            cpu = cpu with
            {
                PerCoreLoad = cpu.PerCoreLoad is { Count: > 0 } ? cpu.PerCoreLoad : threads,
                Load = cpu.Load ?? _cpuFallback.ReadTotalLoad(),
            };
        }

        // Hard guarantee: the thread rings always match the logical processors
        // Windows reports. If the sensor library returns a different count (an
        // aggregate sensor slipped through, or a CPU it only partly knows), the
        // per-thread loads come from Windows instead — on any vendor.
        if (cpu is { PerCoreLoad.Count: > 0 } && cpu.PerCoreLoad.Count != Environment.ProcessorCount)
        {
            IReadOnlyList<double>? windowsThreads = _cpuFallback.ReadThreadLoads();
            if (windowsThreads is { Count: > 0 } && windowsThreads.Count == Environment.ProcessorCount)
            {
                cpu = cpu with { PerCoreLoad = windowsThreads };
            }
        }

        if (cpu is not null && (cpu.ClockMhz is null || cpu.TemperatureC is null))
        {
            double? clock = cpu.ClockMhz ?? _cpuFallback.ReadClockMhz();
            double? acpiTemp = cpu.TemperatureC is null ? _cpuFallback.ReadTemperatureC() : null;
            cpu = cpu with
            {
                ClockMhz = clock,
                TemperatureC = cpu.TemperatureC ?? acpiTemp,
                ApproximateTemperature = cpu.TemperatureC is null && acpiTemp is not null,
            };
        }

        // Physical / commit memory from Windows itself (exact, no admin needed);
        // LibreHardwareMonitor is only a fallback.
        ram = SystemMemory.Read() ?? ram;

        var drives = _options.IncludeDrives ? _driveSampler?.ReadAll() ?? new List<DriveReading>() : new List<DriveReading>();
        var adapters = _options.IncludeNetwork ? _networkSampler?.ReadAll() ?? new List<NetworkAdapterReading>() : new List<NetworkAdapterReading>();

        return new MetricsSnapshot(
            DateTimeOffset.Now, cpu, ram, gpus ?? new List<GpuReading>(), drives, adapters,
            sensors ?? new List<SensorReading>(),
            cpu?.PackagePowerW is null && !_sensorsUnsupported ? _sensorHint : null);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Stop();

        // Closing LHM (driver unload) can take seconds; do it off the caller's
        // thread so quitting the app is never blocked by it.
        Computer? computer = _computer;
        DriveSampler? drives = _driveSampler;
        _computer = null;
        _driveSampler = null;

        Task closeTask = Task.Run(() =>
        {
            try
            {
                _initTask.Wait(TimeSpan.FromSeconds(3));
                computer ??= _computer;
                computer?.Close();
            }
            catch
            {
                // Best-effort close.
            }

            drives?.Dispose();
            _amdPower.Dispose();
            _cpuFallback.Dispose();
        });

        // Give the driver unload a short grace period, but never hold up exit.
        closeTask.Wait(TimeSpan.FromMilliseconds(1200));
    }
}
