using System.Diagnostics;
using System.Windows.Threading;
using SysDash.Core.Models;
using SysDash.Core.Threading;
using SysDash.Core.Units;
using SysDash.Widgets.Common;

namespace SysDash.Widgets.Ram;

/// <summary>
/// Detailed memory widget: usage + commit rings, a usage history strip, a
/// breakdown list and the top memory-using processes (grouped by name).
/// </summary>
public sealed class RamViewModel : HardwareWidgetViewModel
{
    private readonly DispatcherTimer _processTimer;
    private bool _loadingProcesses;
    private bool _isUsageAlert;
    private string _alertText = string.Empty;
    private double _usedPercent;
    private string _usedText = "—";
    private double _virtualPercent;
    private string _virtualText = "—";
    private string _usedBytesText = "—";
    private string _freeBytesText = "—";
    private string _virtualBytesText = "—";
    private string _totalBytesText = "—";
    private IReadOnlyList<ProcessRow> _processes = Array.Empty<ProcessRow>();

    public RamViewModel(IThreadMarshaller marshaller, RamSettings settings)
        : base(marshaller, settings, settings.HistoryCapacity)
    {
        _processTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        _processTimer.Tick += async (_, _) => await RefreshProcessesAsync();

    }

    /// <summary>Settings dialog saved → restart / stop process sampling, resize the graph.</summary>
    protected override void OnSettingsChanged()
    {
        ResizeHistory(Options.HistoryCapacity);

        if (Options.ShowProcesses)
        {
            _processTimer.Start();
            _ = RefreshProcessesAsync();
        }
        else
        {
            _processTimer.Stop();
            Processes = Array.Empty<ProcessRow>();
        }
    }

    public RamSettings Options => (RamSettings)Settings;

    public override string Title => "Memory";

    public bool ShowProcesses => Options.ShowProcesses;

    public bool ShowDetails => Options.ShowDetails;

    public bool ShowGraph => Options.ShowUsageGraph;

    public bool IsUsageAlert { get => _isUsageAlert; private set => SetProperty(ref _isUsageAlert, value); }

    public string AlertText { get => _alertText; private set => SetProperty(ref _alertText, value); }

    public double UsedPercent { get => _usedPercent; private set => SetProperty(ref _usedPercent, value); }

    public string UsedText { get => _usedText; private set => SetProperty(ref _usedText, value); }

    public double VirtualPercent { get => _virtualPercent; private set => SetProperty(ref _virtualPercent, value); }

    public string VirtualText { get => _virtualText; private set => SetProperty(ref _virtualText, value); }

    public string UsedBytesText { get => _usedBytesText; private set => SetProperty(ref _usedBytesText, value); }

    public string FreeBytesText { get => _freeBytesText; private set => SetProperty(ref _freeBytesText, value); }

    public string VirtualBytesText { get => _virtualBytesText; private set => SetProperty(ref _virtualBytesText, value); }

    public string TotalBytesText { get => _totalBytesText; private set => SetProperty(ref _totalBytesText, value); }

    public IReadOnlyList<ProcessRow> Processes { get => _processes; private set => SetProperty(ref _processes, value); }

    protected override void OnActivated()
    {
        if (Options.ShowProcesses)
        {
            _processTimer.Start();
            _ = RefreshProcessesAsync();
        }
    }

    protected override void OnDispose()
    {
        _processTimer.Stop();
    }

    protected override void Update(MetricsSnapshot snapshot)
    {
        RamReading? ram = snapshot.Ram;

        if (ram is null)
        {
            UsedText = "—";
            PushHistory(0);
            return;
        }

        double percent = Math.Clamp(ram.UsedPercent ?? ram.Load ?? 0, 0, 100);
        UsedPercent = percent;
        UsedText = $"{percent:0}%";
        PushHistory(percent);
        IsUsageAlert = percent >= Options.UsageAlertPercent;
        AlertText = IsUsageAlert ? $"{percent:0}% USED" : string.Empty;

        if (ram.VirtualPercent is { } virt)
        {
            VirtualPercent = Math.Clamp(virt, 0, 100);
            VirtualText = $"{virt:0}%";
        }
        else
        {
            VirtualPercent = 0;
            VirtualText = "—";
        }

        UsedBytesText = ram.UsedBytes is { } used ? UnitFormat.Bytes(used) : "—";
        FreeBytesText = ram.FreeBytes is { } free ? UnitFormat.Bytes(free) : "—";
        TotalBytesText = ram.TotalBytes is { } total ? UnitFormat.Bytes(total) : "—";
        VirtualBytesText = ram.VirtualUsedBytes is { } vUsed && ram.VirtualTotalBytes is { } vTotal
            ? $"{UnitFormat.Bytes(vUsed)} / {UnitFormat.Bytes(vTotal)}"
            : "—";
    }

    private async Task RefreshProcessesAsync()
    {
        if (_loadingProcesses)
        {
            return;
        }

        _loadingProcesses = true;
        try
        {
            int count = Math.Clamp(Options.ProcessCount, 1, 30);
            List<ProcessRow> rows = await Task.Run(() => SampleTopProcesses(count));
            Processes = rows;
        }
        catch
        {
            // Process sampling is best-effort.
        }
        finally
        {
            _loadingProcesses = false;
        }
    }

    private static List<ProcessRow> SampleTopProcesses(int count)
    {
        var totals = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        foreach (Process process in Process.GetProcesses())
        {
            try
            {
                string name = process.ProcessName;
                long bytes = process.WorkingSet64;
                totals[name] = totals.TryGetValue(name, out long existing) ? existing + bytes : bytes;
            }
            catch
            {
                // Access denied / process exited — skip it.
            }
            finally
            {
                process.Dispose();
            }
        }

        return totals
            .Where(kv => !kv.Key.Equals("Idle", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(kv => kv.Value)
            .Take(count)
            .Select(kv => new ProcessRow(kv.Key, UnitFormat.Bytes(kv.Value)))
            .ToList();
    }
}
