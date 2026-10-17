using Microsoft.Extensions.DependencyInjection;
using SysDash.SDK.Hosting;
using SysDash.SDK.Widgets;
using SysDash.SDK.WindowManagement;

namespace SysDash.App;

/// <summary>
/// Owns all detached widget windows. Widget windows are always resolved from
/// the DI container so they can be constructed with their service dependencies.
/// </summary>
public sealed class WidgetWindowService : IWidgetWindowService
{
    private readonly IServiceProvider _services;
    private readonly Dictionary<Guid, WidgetWindow> _windows = new();
    private readonly Dictionary<Guid, Guid> _instanceToWindow = new();

    public WidgetWindowService(IServiceProvider services)
    {
        _services = services ?? throw new ArgumentNullException(nameof(services));
    }

    public Guid CreateWindow(WidgetInstance instance, WidgetPlacementSpec placement)
    {
        var window = ActivatorUtilities.CreateInstance<WidgetWindow>(_services, instance, placement);
        Guid id = Guid.NewGuid();
        _windows[id] = window;
        _instanceToWindow[instance.Id] = id;

        window.Closed += (_, _) =>
        {
            _windows.Remove(id);
            _instanceToWindow.Remove(instance.Id);
        };

        window.Show();
        return id;
    }

    public void Close(Guid windowId)
    {
        if (_windows.TryGetValue(windowId, out WidgetWindow? window))
        {
            window.Close();
        }
    }

    public bool TryGetHost(Guid windowId, out IWidgetHost host)
    {
        if (_windows.TryGetValue(windowId, out WidgetWindow? window))
        {
            host = window;
            return true;
        }

        host = null!;
        return false;
    }

    public void BringToFront(Guid windowId)
    {
        if (_windows.TryGetValue(windowId, out WidgetWindow? window))
        {
            window.Activate();
        }
    }

    public void SetAlwaysOnTop(Guid windowId, bool alwaysOnTop)
    {
        if (_windows.TryGetValue(windowId, out WidgetWindow? window))
        {
            window.SetAlwaysOnTop(alwaysOnTop);
        }
    }

    public void SetLayer(Guid windowId, string layer)
    {
        if (_windows.TryGetValue(windowId, out WidgetWindow? window))
        {
            window.SetLayer(SysDash.Theming.Native.WindowLayerHelper.Parse(layer));
        }
    }

    public void SetClickThrough(Guid windowId, bool clickThrough)
    {
        if (_windows.TryGetValue(windowId, out WidgetWindow? window))
        {
            window.SetClickThrough(clickThrough);
        }
    }

    public void SetTheme(Guid windowId, string? theme)
    {
        if (_windows.TryGetValue(windowId, out WidgetWindow? window))
        {
            window.SetWidgetTheme(theme);
        }
    }

    public void SetOpacity(Guid windowId, double opacity)
    {
        if (_windows.TryGetValue(windowId, out WidgetWindow? window))
        {
            window.SetWindowOpacity(opacity);
        }
    }

    public void MoveTo(Guid windowId, double x, double y)
    {
        if (_windows.TryGetValue(windowId, out WidgetWindow? window))
        {
            window.MoveTo(x, y);
        }
    }

    public void CloseAll()
    {
        foreach (WidgetWindow window in _windows.Values.ToArray())
        {
            window.Close();
        }

        _windows.Clear();
        _instanceToWindow.Clear();
    }
}