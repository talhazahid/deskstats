using SysDash.Core.Models;

namespace SysDash.SDK.Session;

/// <summary>
/// The live metrics feed available to widgets. Snapshots arrive on the UI thread.
/// </summary>
public interface ISessionContext
{
    /// <summary>Most recent snapshot received.</summary>
    MetricsSnapshot Latest { get; }

    /// <summary>Raised on the UI thread for every new snapshot.</summary>
    event Action<MetricsSnapshot>? SnapshotReceived;

    bool IsRunning { get; }

    TimeSpan PollInterval { get; }
}