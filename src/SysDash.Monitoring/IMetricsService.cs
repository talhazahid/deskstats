using SysDash.Core.Models;

namespace SysDash.Monitoring;

/// <summary>
/// Publishes hardware status snapshots on a background timer. All hardware access
/// lives behind this interface so UI and widgets never touch LHM directly.
/// </summary>
public interface IMetricsService : IDisposable
{
    /// <summary>Raised after every successful sampling cycle (background thread).</summary>
    event Action<MetricsSnapshot>? SnapshotReady;

    MetricsSnapshot Latest { get; }

    bool IsRunning { get; }

    TimeSpan PollInterval { get; }

    void Start();

    void Stop();

    /// <summary>Live poll-interval change (clamped 200–60000 ms).</summary>
    void ChangePollInterval(int milliseconds);
}