using SysDash.Core.Diagnostics;
using SysDash.Core.Models;
using SysDash.Core.Mvvm;
using SysDash.Core.Threading;
using SysDash.SDK.Session;

namespace SysDash.SDK.Widgets;

/// <summary>
/// Base view model for all widgets. Subscribes to session snapshots while active,
/// marshals updates onto the UI thread, and owns the per-widget settings object.
/// </summary>
public abstract class WidgetViewModel : ObservableObject, IDisposable
{
    private readonly IThreadMarshaller _marshaller;
    private ISessionContext? _session;
    private bool _active;
    private bool _disposed;

    protected WidgetViewModel(IThreadMarshaller marshaller, WidgetSettings settings)
    {
        _marshaller = marshaller ?? throw new ArgumentNullException(nameof(marshaller));
        Settings = settings ?? throw new ArgumentNullException(nameof(settings));
    }

    /// <summary>Typed settings for this widget instance.</summary>
    public WidgetSettings Settings { get; }

    /// <summary>Title shown in window chrome / list views.</summary>
    public abstract string Title { get; }

    /// <summary>Raised when the user edits settings (host re-serializes and saves).</summary>
    public event EventHandler? SettingsChanged;

    /// <summary>Start receiving snapshots.</summary>
    public void Activate(ISessionContext session)
    {
        if (_active)
        {
            return;
        }

        _session = session;
        _active = true;
        session.SnapshotReceived += OnSnapshot;
        OnActivated();
    }

    /// <summary>Stop receiving snapshots (hidden window, shutdown).</summary>
    public void Deactivate()
    {
        if (!_active)
        {
            return;
        }

        _active = false;
        if (_session is not null)
        {
            _session.SnapshotReceived -= OnSnapshot;
            _session = null;
        }
    }

    /// <summary>
    /// Raise when settings change: the widget re-applies them (hook +
    /// refresh of every binding, so Show/Hide toggles take effect at once)
    /// and the host persists them.
    /// </summary>
    public void NotifySettingsChanged()
    {
        try
        {
            OnSettingsChanged();
        }
        catch (Exception ex)
        {
            Log.Error($"[{Title}] applying settings failed", ex);
        }

        // Empty name = "all properties changed": every {Binding} re-reads,
        // including computed ones like ShowDetails => Options.ShowDetails.
        OnPropertyChanged(string.Empty);
        SettingsChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Called after the settings window saved new values.</summary>
    protected virtual void OnSettingsChanged() { }

    private void OnSnapshot(MetricsSnapshot snapshot)
    {
        if (!_active)
        {
            return;
        }

        _marshaller.RunOnUiThread(() =>
        {
            try
            {
                Update(snapshot);
            }
            catch (Exception ex)
            {
                Log.Error($"[{Title}] snapshot update failed", ex);
            }
        });
    }

    protected virtual void OnActivated() { }

    /// <summary>Apply the latest snapshot. Always runs on the UI thread.</summary>
    protected abstract void Update(MetricsSnapshot snapshot);

    /// <summary>Hook for subclasses to release timers/HTTP/streams.</summary>
    protected virtual void OnDispose() { }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Deactivate();
        OnDispose();
    }
}