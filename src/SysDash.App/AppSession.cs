using SysDash.Core.Models;
using SysDash.Monitoring;
using SysDash.SDK.Session;

namespace SysDash.App;

/// <summary>
/// Bridges the background MetricsService to the UI thread and exposes it to
/// widgets through ISessionContext. Snapshots are re-raised on the UI thread.
/// </summary>
public sealed class AppSession : ISessionContext, IDisposable
{
    private readonly IMetricsService _metrics;
    private readonly ThreadMarshaller _marshaller;
    private bool _disposed;

    public AppSession(IMetricsService metrics, ThreadMarshaller marshaller)
    {
        _metrics = metrics;
        _marshaller = marshaller;
        _metrics.SnapshotReady += OnSnapshotReady;
    }

    public event Action<MetricsSnapshot>? SnapshotReceived;

    public MetricsSnapshot Latest => _metrics.Latest;

    public bool IsRunning => _metrics.IsRunning;

    public TimeSpan PollInterval => _metrics.PollInterval;

    private void OnSnapshotReady(MetricsSnapshot snapshot)
        => _marshaller.RunOnUiThread(() => SnapshotReceived?.Invoke(snapshot));

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _metrics.SnapshotReady -= OnSnapshotReady;
    }
}