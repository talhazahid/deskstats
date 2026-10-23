using System.Windows;
using SysDash.Core.Diagnostics;
using SysDash.Layouts;
using SysDash.Layouts.Models;
using SysDash.SDK.Hosting;
using SysDash.SDK.Widgets;
using SysDash.SDK.WindowManagement;
using SysDash.Theming;
using SysDash.Theming.Native;

namespace SysDash.App;

/// <summary>
/// Owns the live widget instances for the active layout.
///
/// Design rule: the controller holds ONE mutable working copy of the layout
/// (<see cref="_current"/>, cloned from <see cref="ILayoutService.ActiveLayout"/>)
/// and ALL edits (add/remove/detach/move/place/flags/settings/theme) target that
/// copy. Persistence is a debounced <c>SaveActive(_current)</c>, so transient
/// drag/resize churn coalesces into a single write. Layout switches from the
/// tray MUST go through <see cref="ActivateLayout"/> — never
/// <see cref="ILayoutService.Activate"/> directly — otherwise the working copy
/// and the persisted active layout would diverge.
/// </summary>
public sealed class LayoutController : IDisposable
{
    private readonly IServiceProvider _services;
    private readonly IWidgetRegistry _registry;
    private readonly ILayoutService _layouts;
    private readonly SidebarWindow _sidebar;
    private readonly IWidgetWindowService _widgetWindows;
    private readonly IThemeService _theme;
    private readonly AppSettings _settings;
    private readonly AppSettingsService _settingsService;

    private readonly Dictionary<Guid, WidgetInstance> _instances = new();
    private readonly Dictionary<Guid, Guid> _detachedWindows = new();

    private Layout _current;
    private bool _layingOutAll;
    private CancellationTokenSource? _saveCts;
    private readonly HashSet<Guid> _initialArrangement = new();
    private bool _arrangingInitial;
    private bool _tearingDown;

    public LayoutController(
        IServiceProvider services,
        IWidgetRegistry registry,
        ILayoutService layouts,
        SidebarWindow sidebar,
        IWidgetWindowService widgetWindows,
        IThemeService theme,
        AppSettings settings,
        AppSettingsService settingsService)
    {
        _services = services ?? throw new ArgumentNullException(nameof(services));
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _layouts = layouts ?? throw new ArgumentNullException(nameof(layouts));
        _sidebar = sidebar ?? throw new ArgumentNullException(nameof(sidebar));
        _widgetWindows = widgetWindows ?? throw new ArgumentNullException(nameof(widgetWindows));
        _theme = theme ?? throw new ArgumentNullException(nameof(theme));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
        _current = _layouts.ActiveLayout;
    }

    // ---- status ------------------------------------------------------------

    public string CurrentLayoutName => _current.Name;

    /// <summary>True when at least one widget of the active layout is shown (not hidden).</summary>
    public bool HasVisibleWidgets => _current.Items.Any(i => !i.Placement.Hidden);

    public IReadOnlyList<string> LayoutNames => _layouts.LayoutNames;

    /// <summary>All live instances, ordered by title (for menus).</summary>
    public IReadOnlyList<WidgetInstance> Instances
        => _instances.Values.OrderBy(i => i.EffectiveTitle, StringComparer.OrdinalIgnoreCase).ToArray();

    /// <summary>
    /// All persisted items of the active layout, including hidden ones — the
    /// tray menu lists these so hidden widgets stay reachable (P1).
    /// </summary>
    public IReadOnlyList<LayoutItem> LayoutItems => _current.Items;

    public bool IsDocked(Guid instanceId)
        => FindItem(instanceId)?.Placement.Docked ?? false;

    /// <summary>True when the item exists but is not shown (no view; P1 Hidden state).</summary>
    public bool IsHidden(Guid instanceId)
        => FindItem(instanceId)?.Placement.Hidden ?? false;

    public bool IsGridSnapEnabled(Guid instanceId)
        => FindItem(instanceId)?.Placement.SnapToGrid ?? true;

    // ---- layout activation -------------------------------------------------

    /// <summary>Loads the current persisted layout into instances (startup path).</summary>
    public void ApplyActiveLayout()
    {
        _current = _layouts.ActiveLayout;
        EnsureDefaultWidgets();
        Materialize();
    }

    /// <summary>The hardware widgets every fresh / empty layout starts with.</summary>
    public static readonly string[] DefaultWidgetIds = { "cpu", "gpu", "ram", "network", "drive", "sensors" };

    /// <summary>Restore the complete hardware set without moving existing widgets.</summary>
    public void ShowAllHardwareWidgets()
    {
        foreach (string id in DefaultWidgetIds)
        {
            if (IsWidgetShown(id)) continue;
            var hidden = _current.Items.FirstOrDefault(i => i.WidgetId.Equals(id, StringComparison.OrdinalIgnoreCase));
            if (hidden is not null) ShowInstance(hidden.InstanceId);
            else AddWidget(id, docked: IsSidebarMode);
        }
        ScheduleSave();
    }

    /// <summary>
    /// An empty layout would launch to a blank desktop. Seed it with the
    /// hardware widgets (floating, or docked in sidebar mode) instead.
    /// </summary>
    private void EnsureDefaultWidgets()
    {
        if (_current.Items.Count > 0 || _current.HasSavedState)
        {
            return;
        }

        foreach (string id in DefaultWidgetIds)
        {
            if (_registry.Find(id) is null)
            {
                continue;
            }

            _current.Items.Add(new LayoutItem
            {
                WidgetId = id,
                SettingsJson = string.Empty,
                Placement = IsSidebarMode
                    ? new WidgetPlacement { Docked = true }
                    : new WidgetPlacement { Docked = false, AutoPlace = true, Layer = "normal" },
            });
        }

        Log.Info($"Layout '{_current.Name}' was empty — added the default hardware widgets.");
        ScheduleSave();
    }

    /// <summary>True when a widget of this type is currently shown (for the ✓ in the Add widget menu).</summary>
    public bool IsClickThrough(Guid instanceId) => FindItem(instanceId)?.Placement.ClickThrough ?? false;

    /// <summary>Tray toggle for click-through (the widget itself can't be clicked while it is on).</summary>
    public void SetClickThrough(Guid instanceId, bool clickThrough)
    {
        if (_detachedWindows.TryGetValue(instanceId, out Guid windowId))
        {
            _widgetWindows.SetClickThrough(windowId, clickThrough);
        }
    }

    public bool IsWidgetShown(string widgetId)
        => _current.Items.Any(i => !i.Placement.Hidden &&
                                   i.WidgetId.Equals(widgetId, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Add-widget menu toggle: a shown widget type is hidden (all its instances,
    /// settings kept); a hidden one is shown again; a missing one is added.
    /// </summary>
    public void ToggleWidget(string widgetId)
    {
        List<LayoutItem> ofType = _current.Items
            .Where(i => i.WidgetId.Equals(widgetId, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (ofType.Any(i => !i.Placement.Hidden))
        {
            foreach (LayoutItem item in ofType.Where(i => !i.Placement.Hidden))
            {
                HideInstance(item.InstanceId);
            }

            return;
        }

        LayoutItem? hidden = ofType.FirstOrDefault();
        if (hidden is not null)
        {
            // Bring it back where the current display mode wants it.
            hidden.Placement.Docked = IsSidebarMode;
            if (!IsSidebarMode)
            {
                hidden.Placement.AutoPlace = true;
            }

            ShowInstance(hidden.InstanceId);
            return;
        }

        AddWidget(widgetId, docked: IsSidebarMode);
    }

    /// <summary>
    /// Switches to a saved/preset layout. The only legal entry point for layout
    /// changes — directly calling <see cref="ILayoutService.Activate"/> would
    /// leave <see cref="_current"/> out of sync with the activated layout.
    /// </summary>
    public bool ActivateLayout(string name)
    {
        if (!_layouts.Activate(name))
        {
            Log.Warn($"ActivateLayout: unknown layout '{name}'");
            return false;
        }

        _current = _layouts.ActiveLayout;
        ApplyLayoutTheme();
        _settings.ActiveLayout = _current.Name;
        _settingsService.Save();
        Materialize();
        Log.Info($"Layout controller switched to '{name}' ({_current.Items.Count} items)");
        return true;
    }

    /// <summary>Saves the current configuration under a new (non-preset) name and switches to it.</summary>
    public bool SaveCurrentAs(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || LayoutPresets.IsPreset(name))
        {
            Log.Warn($"SaveCurrentAs: '{name}' is empty or collides with a preset.");
            return false;
        }

        try
        {
            _layouts.SaveAs(_current, name);       // write file under the new name
            _layouts.SaveActive(_current.Clone()); // repoint active.txt + keep file in sync
            _settings.ActiveLayout = _current.Name;
            _settingsService.Save();
            Log.Info($"Saved current layout as '{name}'");
            return true;
        }
        catch (Exception ex)
        {
            Log.Error($"SaveCurrentAs failed for '{name}'", ex);
            return false;
        }
    }

    /// <summary>Theme switch from the tray: applied live and folded into the current layout.</summary>
    public void SetTheme(ThemeKind kind)
    {
        _theme.Apply(Application.Current.Resources, kind);
        _settings.Theme = kind == ThemeKind.Dark ? "dark" : "light";
        _settingsService.Save();
        _current.Theme = _settings.Theme;
        ScheduleSave();
    }

    private void ApplyLayoutTheme()
    {
        if (string.IsNullOrWhiteSpace(_current.Theme))
        {
            return;
        }

        ThemeKind kind = _current.Theme.Equals("light", StringComparison.OrdinalIgnoreCase)
            ? ThemeKind.Light
            : ThemeKind.Dark;
        _theme.Apply(Application.Current.Resources, kind);
    }

    // ---- materialization ---------------------------------------------------

    /// <summary>
    /// Turns the persisted layout items into runtime instances. Unknown widget
    /// ids are dropped (and the drop persisted), and a settings blob that fails
    /// to deserialize falls back to the widget defaults.
    /// </summary>
    public void Materialize()
    {
        StartupTrace.Mark($"materialize: {_current.Items.Count} item(s)");
        ClearRuntime();
        _initialArrangement.Clear();
        var floating = _current.Items.Where(i => !i.Placement.Docked && !i.Placement.Hidden).ToArray();
        if (floating.Length > 0 && floating.All(i => i.Placement.AutoPlace))
        {
            foreach (var item in floating) _initialArrangement.Add(item.InstanceId);
            ArrangeInitialWindows();
        }

        bool droppedUnknown = false;
        foreach (LayoutItem item in _current.Items.ToArray())
        {
            if (item.Placement.Hidden)
            {
                StartupTrace.Mark($"materialize '{item.WidgetId}' (hidden)");
                continue;
            }

            if (MaterializeItem(item) is null)
            {
                Log.Warn($"Materialize: skipping unknown widget id '{item.WidgetId}'");
                _current.Items.Remove(item);
                droppedUnknown = true;
                continue;
            }

            StartupTrace.Mark($"materialize '{item.WidgetId}' ({(item.Placement.Docked ? "docked" : "detached")})");
        }

        if (droppedUnknown)
        {
            ScheduleSave();
        }

        SyncSidebarVisibility();
        StartupTrace.Mark("materialize done");
    }

    /// <summary>
    /// Creates the runtime instance for one non-hidden layout item and hosts it
    /// either in the sidebar or in a floating window. Returns null when the
    /// widget id is no longer registered. Shared by <see cref="Materialize"/>
    /// and <see cref="ShowInstance"/>.
    /// </summary>
    private WidgetInstance? MaterializeItem(LayoutItem item)
    {
        IWidget? widget = _registry.Find(item.WidgetId);
        if (widget is null)
        {
            return null;
        }

        WidgetSettings? settings;
        try
        {
            settings = widget.DeserializeSettings(item.SettingsJson) ?? widget.CreateDefaultSettings();
        }
        catch (Exception ex)
        {
            Log.Warn($"Materialize: settings for '{item.WidgetId}' invalid — using defaults. {ex.Message}");
            settings = widget.CreateDefaultSettings();
        }

        var instance = new WidgetInstance
        {
            Id = item.InstanceId,
            WidgetId = item.WidgetId,
            Widget = widget,
            Services = _services,
            Settings = settings,
            TitleOverride = item.TitleOverride,
        };
        widget.CreateView(instance);
        instance.View.LayoutTransform = new System.Windows.Media.ScaleTransform(item.Placement.LargeSize ? 1.35 : 1, item.Placement.LargeSize ? 1.35 : 1);
        instance.ViewModel.SettingsChanged += (_, _) => PersistSettings(instance);
        _instances[instance.Id] = instance;

        if (item.Placement.Docked)
        {
            _sidebar.AddItem(instance);
        }
        else
        {
            if (item.Placement.AutoPlace)
            {
                if (_initialArrangement.Contains(item.InstanceId)) item.Placement.AutoPlace = false;
                else ResolveAutoPlace(item, instance.View, measure: true);
                ScheduleSave();
            }

            HostFloatingOrDock(instance, item);
        }

        return instance;
    }

    /// <summary>
    /// Keeps the sidebar container in sync with the layout (P1): it is shown
    /// only while it hosts at least one visible docked widget, so a
    /// floating-first setup never leaves an empty full-height shell on the
    /// desktop. Auto-hide is unaffected because it animates Width, not
    /// Visibility.
    /// </summary>
    private void SyncSidebarVisibility()
    {
        bool hasDocked = _current.Items.Any(i => i.Placement.Docked && !i.Placement.Hidden);
        if (hasDocked)
        {
            if (!_sidebar.IsVisible)
            {
                _sidebar.Show();
                Log.Info("Sidebar shown (has visible docked widgets).");
            }
            _sidebar.Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded,
                new Action(MoveFloatingWidgetsClearOfSidebar));
        }
        else if (_sidebar.IsVisible)
        {
            _sidebar.Hide();
            Log.Info("Sidebar hidden (no visible docked widgets).");
        }
    }

    // ---- instance mutation -------------------------------------------------
    private void MoveFloatingWidgetsClearOfSidebar()
    {
        if (_tearingDown || !_sidebar.IsVisible) return;
        _sidebar.UpdateLayout();
        Rect sidebar = new(_sidebar.Left, _sidebar.Top, _sidebar.ActualWidth, _sidebar.ActualHeight);
        if (sidebar.IsEmpty || sidebar.Width <= 0 || sidebar.Height <= 0) return;
        BeginManualPlacement();
        var windows = _detachedWindows.Values.Select(id =>
            _widgetWindows.TryGetHost(id, out var host) ? host as WidgetWindow : null)
            .Where(w => w is not null && w.IsVisible).Cast<WidgetWindow>().ToArray();
        foreach (var window in windows)
        {
            Rect bounds = new(window.Left, window.Top, window.ActualWidth, window.ActualHeight);
            if (!bounds.IntersectsWith(sidebar)) continue;
            var monitor = MonitorGeometry.MonitorAt(new Point(sidebar.Left + sidebar.Width / 2, sidebar.Top + 20));
            Rect work = monitor?.WorkAreaDip ?? MonitorGeometry.PrimaryWorkAreaDip();
            var obstacles = windows.Where(w => w != window)
                .Select(w => new Rect(w.Left, w.Top, w.ActualWidth, w.ActualHeight)).Append(sidebar).ToArray();
            Point? destination = null;
            // Search available space with a consistent gap, keeping unaffected widgets still.
            for (double y = work.Top + 12; y + bounds.Height <= work.Bottom - 12 && destination is null; y += 12)
                for (double x = work.Left + 12; x + bounds.Width <= work.Right - 12; x += 12)
                {
                    Rect candidate = new(x, y, bounds.Width, bounds.Height);
                    candidate.Inflate(6, 6);
                    if (obstacles.All(o => !candidate.IntersectsWith(o))) { destination = new Point(x, y); break; }
                }
            // If the desktop is full, prioritize clearing the sidebar without clamping back onto it.
            destination ??= new Point(sidebar.Left - bounds.Width - 12 >= work.Left
                ? sidebar.Left - bounds.Width - 12 : sidebar.Right + 12, bounds.Top);
            window.MoveTo(destination.Value.X, destination.Value.Y);
            window.CapturePlacement();
        }
        ScheduleSave();
    }

    public WidgetInstance? AddWidget(
        string widgetId,
        bool docked = false,
        WidgetSettings? settings = null,
        string? titleOverride = null,
        WidgetPlacement? placement = null)
    {
        IWidget? widget = _registry.Find(widgetId);
        if (widget is null)
        {
            Log.Warn($"AddWidget: unknown widget id '{widgetId}'");
            return null;
        }

        settings ??= widget.CreateDefaultSettings();

        var instance = new WidgetInstance
        {
            WidgetId = widgetId,
            Widget = widget,
            Services = _services,
            Settings = settings,
            TitleOverride = titleOverride,
        };
        widget.CreateView(instance);
        instance.ViewModel.SettingsChanged += (_, _) => PersistSettings(instance);

        var item = new LayoutItem
        {
            InstanceId = instance.Id,
            WidgetId = widgetId,
            TitleOverride = titleOverride,
            SettingsJson = widget.SerializeSettings(instance.Settings),
            Placement = placement ?? (docked
                ? new WidgetPlacement { Docked = true }
                : new WidgetPlacement { Docked = false, AutoPlace = true, Layer = "normal" }),
        };
        _current.Items.Add(item);
        _instances[instance.Id] = instance;

        if (item.Placement.Docked)
        {
            _sidebar.AddItem(instance);
        }
        else
        {
            if (item.Placement.AutoPlace)
            {
                ResolveAutoPlace(item, instance.View, measure: true);
            }

            HostFloatingOrDock(instance, item);
        }

        Log.Info($"Added widget '{widgetId}' ({(item.Placement.Docked ? "docked" : "floating")})");
        ScheduleSave();
        SyncSidebarVisibility();
        return instance;
    }

    public WidgetInstance? DuplicateWidget(Guid instanceId)
    {
        WidgetInstance? source = FindInstance(instanceId);
        if (source is null)
        {
            return null;
        }

        LayoutItem? sourceItem = FindItem(instanceId);
        bool docked = sourceItem?.Placement.Docked ?? false;

        WidgetSettings? settingsCopy = null;
        try
        {
            settingsCopy = source.Widget.DeserializeSettings(source.Widget.SerializeSettings(source.Settings));
        }
        catch (Exception ex)
        {
            Log.Warn($"DuplicateWidget: could not copy settings — using defaults. {ex.Message}");
        }

        // Detached duplicates appear slightly offset so they do not sit on top
        // of the original.
        WidgetPlacement? placement = null;
        if (!docked && sourceItem is not null)
        {
            WidgetPlacement p = sourceItem.Placement;
            placement = new WidgetPlacement
            {
                Docked = false,
                X = p.X + 28,
                Y = p.Y + 28,
                MonitorDeviceId = p.MonitorDeviceId,
                MonitorOffsetX = p.MonitorOffsetX + 28,
                MonitorOffsetY = p.MonitorOffsetY + 28,
                HeightLimit = p.HeightLimit,
                Width = p.Width,
                Height = p.Height,
                Opacity = p.Opacity,
                AlwaysOnTop = p.AlwaysOnTop,
                Layer = p.Layer,
                Theme = p.Theme,
                LargeSize = p.LargeSize,
                ClickThrough = p.ClickThrough,
                SnapToGrid = p.SnapToGrid,
                MonitorIndex = p.MonitorIndex,
            };
        }

        return AddWidget(source.WidgetId, docked, settingsCopy, source.TitleOverride, placement);
    }

    /// <summary>
    /// Detaches a docked widget into its own floating window, centered on the
    /// primary work area. The sidebar slot is removed first so the same view is
    /// re-homed inside the widget window's chrome.
    /// </summary>
    public bool DetachInstance(Guid instanceId)
    {
        LayoutItem? item = FindItem(instanceId);
        WidgetInstance? instance = FindInstance(instanceId);
        if (item is null || instance is null || !item.Placement.Docked)
        {
            return false;
        }

        _sidebar.RemoveItem(instanceId);
        item.Placement.Docked = false;
        item.Placement.AutoPlace = true;
        item.Placement.Layer ??= "normal";
        ResolveAutoPlace(item, instance.View, measure: true);

        HostFloatingOrDock(instance, item);
        Log.Info($"Detached widget '{instance.EffectiveTitle}' to {item.Placement.X:0},{item.Placement.Y:0}");
        ScheduleSave();

        // The sidebar must not linger as an empty shell once its last widget left.
        SyncSidebarVisibility();
        return true;
    }

    /// <summary>True when the app is in "sidebar" mode (new widgets dock into the sidebar).</summary>
    public bool IsSidebarMode => _settings.DisplayMode.Equals("sidebar", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// "Switch to Sidebar": every visible widget is docked into the sidebar,
    /// stacked one above the other. New widgets then dock too.
    /// </summary>
    public void SwitchToSidebar()
    {
        _settings.DisplayMode = "sidebar";
        _settingsService.Save();

        // One widget failing to dock must never leave the others floating on top
        // of the sidebar: each is docked on its own and errors are only logged.
        foreach (LayoutItem item in _current.Items.ToArray())
        {
            if (!item.Placement.Docked && !item.Placement.Hidden)
            {
                try
                {
                    DockInstance(item.InstanceId);
                }
                catch (Exception ex)
                {
                    Log.Error($"Could not dock '{item.WidgetId}' into the sidebar.", ex);
                }
            }
        }

        // Safety net: no floating window may survive for a widget that is now docked.
        foreach (LayoutItem item in _current.Items.ToArray())
        {
            if (item.Placement.Docked && _detachedWindows.Remove(item.InstanceId, out Guid stale))
            {
                Log.Warn($"Closing a leftover floating window of docked widget '{item.WidgetId}'.");
                try
                {
                    if (_widgetWindows.TryGetHost(stale, out IWidgetHost? leftover))
                    {
                        leftover.Closed -= OnHostClosed;
                    }

                    _widgetWindows.Close(stale);
                }
                catch (Exception ex)
                {
                    Log.Error("Leftover window close failed.", ex);
                }
            }
        }

        SyncSidebarVisibility();
        _sidebar.ApplySettings(); // re-snap to the screen edge after the content changed
        Log.Info("Switched to sidebar mode.");
    }

    /// <summary>
    /// "Switch to Widgets": every docked widget becomes its own floating window,
    /// laid out tidily on the desktop and freely draggable. The sidebar hides.
    /// </summary>
    public void SwitchToWidgets()
    {
        _settings.DisplayMode = "widgets";
        _settingsService.Save();

        // The sidebar is about to disappear: lay widgets out over the full width.
        _layingOutAll = true;
        try
        {
            foreach (LayoutItem item in _current.Items.ToArray())
            {
                if (item.Placement.Docked && !item.Placement.Hidden)
                {
                    DetachInstance(item.InstanceId);
                }
            }
        }
        finally
        {
            _layingOutAll = false;
        }

        SyncSidebarVisibility();
        Log.Info("Switched to widgets mode.");
    }

    /// <summary>
    /// Re-docks a detached widget into the sidebar shell (reverse of
    /// <see cref="DetachInstance"/>). Its floating window is closed.
    /// </summary>
    public bool DockInstance(Guid instanceId)
    {
        LayoutItem? item = FindItem(instanceId);
        WidgetInstance? instance = FindInstance(instanceId);
        if (item is null || instance is null || item.Placement.Docked || item.Placement.Hidden)
        {
            return false;
        }

        if (_detachedWindows.Remove(instanceId, out Guid windowId))
        {
            // Re-home without the Close() → OnHostClosed → RemoveInstance
            // reentrancy: this instance must survive its window, unlike in
            // RemoveInstance/HideInstance where it is dropped from _instances
            // first (which makes the reentrant call a no-op).
            if (_widgetWindows.TryGetHost(windowId, out IWidgetHost? host))
            {
                host.Closed -= OnHostClosed;
            }

            try
            {
                _widgetWindows.Close(windowId);
            }
            catch (Exception ex)
            {
                // The window must go no matter what, or it stays on screen behind the sidebar.
                Log.Error($"Closing the floating window of '{instance.EffectiveTitle}' failed.", ex);
                if (host is System.Windows.Window w)
                {
                    try { w.Hide(); } catch (InvalidOperationException) { }
                }
            }
        }

        item.Placement.Docked = true;
        ViewParenting.Release(instance.View); // never let a stale parent block docking
        _sidebar.AddItem(instance);
        Log.Info($"Docked widget '{instance.EffectiveTitle}' into the sidebar");
        ScheduleSave();
        SyncSidebarVisibility();
        return true;
    }

    /// <summary>
    /// Hides a widget without removing it: the view/window goes away, but the
    /// instance stays in the layout (settings, position) and monitoring keeps
    /// running. The persisted flag makes the state survive restarts (P1).
    /// </summary>
    public bool HideInstance(Guid instanceId)
    {
        LayoutItem? item = FindItem(instanceId);
        if (item is null || item.Placement.Hidden)
        {
            return false;
        }

        item.Placement.Hidden = true;
        if (_instances.Remove(instanceId, out WidgetInstance? instance))
        {
            if (_detachedWindows.Remove(instanceId, out Guid windowId))
            {
                _widgetWindows.Close(windowId);
            }
            else
            {
                _sidebar.RemoveItem(instanceId);
            }

            instance.ViewModel.Dispose();
        }

        Log.Info($"Hid widget '{item.WidgetId}'");
        ScheduleSave();
        SyncSidebarVisibility();
        return true;
    }

    /// <summary>Creates the view again for a hidden widget (see <see cref="HideInstance"/>).</summary>
    public bool ShowInstance(Guid instanceId)
    {
        LayoutItem? item = FindItem(instanceId);
        if (item is null || !item.Placement.Hidden)
        {
            return false;
        }

        item.Placement.Hidden = false;
        if (MaterializeItem(item) is null)
        {
            item.Placement.Hidden = true; // unknown widget — leave everything as it was
            Log.Warn($"ShowInstance: widget '{item.WidgetId}' is not registered.");
            return false;
        }

        Log.Info($"Showed widget '{item.WidgetId}'");
        ScheduleSave();
        SyncSidebarVisibility();
        return true;
    }

    /// <summary>
    /// Moves every visible floating widget into a tidy cascade on the primary
    /// monitor — one-click recovery for widgets dragged off-screen (tray
    /// "Reset widget positions", P1). Docked widgets are left untouched.
    /// Returns the number of widgets moved.
    /// </summary>
    public int ResetFloatPositions()
    {
        List<LayoutItem> floating = _current.Items
            .Where(i => !i.Placement.Docked && !i.Placement.Hidden)
            .ToList();
        if (floating.Count == 0)
        {
            return 0;
        }

        // Pass 1: mark everything as "unplaced" so none of them blocks the others.
        foreach (LayoutItem item in floating)
        {
            item.Placement.AutoPlace = true;
        }

        // Pass 2: lay them out one by one and move the live windows.
        foreach (LayoutItem item in floating)
        {
            FrameworkElement? view = FindInstance(item.InstanceId)?.View;
            ResolveAutoPlace(item, view, measure: view is not null);
            item.Placement.MonitorIndex = 0;

            if (_detachedWindows.TryGetValue(item.InstanceId, out Guid windowId))
            {
                _widgetWindows.MoveTo(windowId, item.Placement.X, item.Placement.Y);
            }
        }

        Log.Info($"Reset positions of {floating.Count} floating widget(s)");
        ScheduleSave();
        return floating.Count;
    }

    /// <summary>
    /// Restores the active layout to its built-in preset content (only for the
    /// preset names) — recovery when a saved copy of a preset is outdated.
    /// </summary>
    public bool ResetToPreset()
    {
        if (!LayoutPresets.IsPreset(_current.Name))
        {
            return false;
        }

        Layout fresh = LayoutPresets.Build(
            LayoutPresets.Names.First(n => n.Equals(_current.Name, StringComparison.OrdinalIgnoreCase)));
        _saveCts?.Cancel();
        _layouts.SaveActive(fresh);
        _current = _layouts.ActiveLayout;
        ApplyLayoutTheme();
        Materialize();
        Log.Info($"Layout '{_current.Name}' reset to its preset.");
        return true;
    }

    /// <summary>
    /// Removes a widget instance. Reentrancy-safe: the instance is removed from
    /// <c>_instances</c> first, so the <see cref="IWidgetHost.Closed"/> event
    /// raised while closing a window triggers a harmless no-op second call.
    /// </summary>
    public void RemoveInstance(Guid instanceId)
    {
        if (!_instances.Remove(instanceId, out WidgetInstance? instance))
        {
            return; // already removed (window-close reentrancy)
        }

        _current.Items.RemoveAll(i => i.InstanceId == instanceId);

        // Capture the window id BEFORE dropping the mapping, then close the
        // window. Closing re-raises host.Closed → RemoveInstance, which now
        // no-ops because the instance is already out of _instances.
        bool wasDetached = _detachedWindows.Remove(instanceId, out Guid windowId);
        if (wasDetached)
        {
            _widgetWindows.Close(windowId);
        }
        else
        {
            _sidebar.RemoveItem(instanceId);
        }

        instance.ViewModel.Dispose();
        Log.Info($"Removed widget '{instance.EffectiveTitle}' ({instance.WidgetId})");
        ScheduleSave();
    }

    public void ClearAll()
    {
        foreach (Guid id in _instances.Keys.ToArray())
        {
            RemoveInstance(id);
        }
    }

    // ---- detached placement / flags ---------------------------------------

    public void SetDetachedPosition(Guid instanceId, double x, double y, int? screenNumber = null)
    {
        if (FindItem(instanceId) is { } item)
        {
            item.Placement.X = x;
            item.Placement.Y = y;
            item.Placement.AutoPlace = false;
            if (screenNumber.HasValue)
            {
                item.Placement.MonitorIndex = screenNumber.Value;
            }

            ScheduleSave();
        }
    }

    public void SetDetachedSize(Guid instanceId, double width, double height)
    {
        if (FindItem(instanceId) is { } item)
        {
            if (Math.Abs(item.Placement.Width - width) < 0.1 && Math.Abs(item.Placement.Height - height) < 0.1) return;
            item.Placement.Width = width;
            item.Placement.Height = height;
            ScheduleSave();
            if (!_tearingDown && _initialArrangement.Contains(instanceId) && !_arrangingInitial) ArrangeInitialWindows();
        }
    }

    /// <summary>
    /// Card frame on/off for every widget (floating + sidebar), applied live and saved.
    /// </summary>
    public void SetCardFrame(bool show)
    {
        _settings.ShowCardFrame = show;
        _settingsService.Save();
        foreach (Window window in Application.Current.Windows)
        {
            if (window is WidgetWindow widget)
            {
                widget.RefreshCardAppearance();
            }
        }

        _sidebar.ApplySettings();
    }

    public bool ShowCardFrame => _settings.ShowCardFrame;

    /// <summary>Stores a floating widget's own theme ("dark" / "light", or null = follow the app).</summary>
    public void SetDetachedTheme(Guid instanceId, string? theme)
    {
        if (FindItem(instanceId) is { } item)
        {
            item.Placement.Theme = string.IsNullOrWhiteSpace(theme) ? null : theme.Trim().ToLowerInvariant();
            ScheduleSave();
        }
    }

    public void SetDetachedFlags(Guid instanceId, bool? alwaysOnTop = null, bool? clickThrough = null, double? opacity = null, string? layer = null)
    {
        if (FindItem(instanceId) is { } item)
        {
            WidgetPlacement p = item.Placement;
            if (alwaysOnTop.HasValue)
            {
                p.AlwaysOnTop = alwaysOnTop.Value;
                p.Layer = alwaysOnTop.Value ? "top" : "normal";
            }

            if (layer is not null)
            {
                p.Layer = layer;
                p.AlwaysOnTop = layer == "top";
            }

            if (clickThrough.HasValue)
            {
                p.ClickThrough = clickThrough.Value;
            }

            if (opacity.HasValue)
            {
                p.Opacity = Math.Clamp(opacity.Value, 0.0, 1.0);
            }

            ScheduleSave();
        }
    }

    // ---- settings ----------------------------------------------------------

    public void OpenSettings(Guid instanceId)
    {
        WidgetInstance? instance = FindInstance(instanceId);
        if (instance is null)
        {
            return;
        }

        // Floating widgets also get the live "Window" card (opacity / layer / click-through).
        WidgetAppearance? appearance = null;
        if (FindItem(instanceId) is { Placement.Docked: false } floating &&
            _detachedWindows.TryGetValue(instanceId, out Guid windowId))
        {
            WidgetPlacement p = floating.Placement;
            appearance = new WidgetAppearance
            {
                Opacity = p.Opacity,
                Layer = p.Layer ?? (p.AlwaysOnTop ? "top" : "normal"),
                ClickThrough = p.ClickThrough,
                Theme = p.Theme ?? string.Empty,
                ApplyTheme = value => _widgetWindows.SetTheme(windowId, value),
                ApplyOpacity = value => _widgetWindows.SetOpacity(windowId, value),
                ApplyLayer = value => _widgetWindows.SetLayer(windowId, value),
                ApplyClickThrough = value => _widgetWindows.SetClickThrough(windowId, value),
            };
        }

        var dialog = new SettingsEditorWindow(instance, appearance);
        dialog.Topmost = true;
        if (_sidebar.IsVisible)
        {
            dialog.Owner = _sidebar;
        }

        // Every Save applies immediately; the window stays open until the user closes it.
        dialog.Saved += () =>
        {
            // Raise so the VM can re-apply (SettingsChanged also persists via the
            // handler wired at creation time).
            instance.ViewModel.NotifySettingsChanged();
            Log.Info($"Settings edited for '{instance.EffectiveTitle}'");
        };

        dialog.ShowDialog();
    }

    private void PersistSettings(WidgetInstance instance)
    {
        LayoutItem? item = FindItem(instance.Id);
        if (item is null)
        {
            return;
        }

        try
        {
            item.SettingsJson = instance.Widget.SerializeSettings(instance.Settings);
        }
        catch (Exception ex)
        {
            Log.Warn($"PersistSettings failed for '{instance.EffectiveTitle}'. {ex.Message}");
        }

        ScheduleSave();
    }

    // ---- persistence -------------------------------------------------------

    /// <summary>
    /// Debounced save of the working layout. The snapshot is cloned on the
    /// calling (UI) thread so the background write never races with in-flight
    /// edits such as window drags; each call cancels the previous delay.
    /// </summary>
    private void ScheduleSave()
    {
        if (_tearingDown) return;
        _saveCts?.Cancel();
        var cts = new CancellationTokenSource();
        _saveCts = cts;

        Layout snapshot = _current.Clone();
        _ = SaveAfterDelay(snapshot, cts);
    }

    private async Task SaveAfterDelay(Layout snapshot, CancellationTokenSource cts)
    {
        try
        {
            await Task.Delay(500, cts.Token);
            try
            {
                _layouts.SaveActive(snapshot);
            }
            catch (Exception ex)
            {
                Log.Error("Failed to save active layout", ex);
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    // ---- teardown ----------------------------------------------------------

    public void FlushState()
    {
        foreach (Guid windowId in _detachedWindows.Values.ToArray())
            if (_widgetWindows.TryGetHost(windowId, out var host) && host is WidgetWindow window) window.CapturePlacement();
        _saveCts?.Cancel();
        _layouts.SaveActive(_current.Clone());
    }

    public void Dispose()
    {
        if (_tearingDown) return;
        _tearingDown = true;
        foreach (Guid windowId in _detachedWindows.Values.ToArray())
            if (_widgetWindows.TryGetHost(windowId, out var host) && host is WidgetWindow window) window.CapturePlacement();
        _saveCts?.Cancel();
        _saveCts = null;

        // Persist the FULL configuration before tearing instances down.
        try
        {
            _layouts.SaveActive(_current.Clone());
        }
        catch (Exception ex)
        {
            Log.Error("Failed to persist layout at shutdown", ex);
        }

        ClearRuntime();

        _saveCts?.Cancel(); // drop debounced saves scheduled by teardown
    }

    // ---- internals ---------------------------------------------------------

    /// <summary>
    /// Assigns a tidy on-screen position (and, optionally, a size that fits the
    /// widget's content) to an item flagged <see cref="WidgetPlacement.AutoPlace"/>.
    /// Widgets are stacked in columns starting at the right edge of the primary
    /// work area (next to the sidebar when it hosts widgets), never overlapping
    /// widgets that are already placed.
    /// </summary>
    private void ResolveAutoPlace(LayoutItem item, FrameworkElement? view, bool measure)
    {
        WidgetPlacement p = item.Placement;
        const double outer = 20;     // 2 x 10 px card margin inside the window
        const double gap = 12;
        const double margin = 16;

        if (measure)
        {
            // Keep a width the user chose; otherwise use the standard widget width.
            if (p.Width < 220 || p.Width > 1200)
            {
                p.Width = 320;
            }

            double desired = 0;
            try
            {
                if (view is not null)
                {
                    view.Measure(new Size(p.Width - outer, double.PositiveInfinity));
                    desired = view.DesiredSize.Height;
                }
            }
            catch (Exception ex)
            {
                Log.Warn($"ResolveAutoPlace: measure failed — using default height. {ex.Message}");
            }

            p.Height = desired > 40 ? Math.Ceiling(desired) + outer : 200;
        }

        double w = Math.Clamp(p.Width, 200, 1200);
        double h = Math.Clamp(p.Height, 120, 1200);

        Rect work = MonitorGeometry.PrimaryWorkAreaDip();
        var occupied = new List<Rect>();

        foreach (LayoutItem other in _current.Items)
        {
            if (ReferenceEquals(other, item) || other.Placement.Docked || other.Placement.Hidden || other.Placement.AutoPlace)
            {
                continue;
            }

            occupied.Add(new Rect(
                other.Placement.X, other.Placement.Y,
                Math.Clamp(other.Placement.Width, 200, 1200),
                Math.Clamp(other.Placement.Height, 120, 1200)));
        }

        double rightLimit = work.Right - margin;
        double leftLimit = work.Left + margin;
        if (!_layingOutAll && MonitorGeometry.IsMainScreen(_settings.SidebarScreenId) &&
            _current.Items.Any(i => i.Placement.Docked && !i.Placement.Hidden))
        {
            double side = Math.Clamp(_settings.SidebarWidth, 240, 560) + margin;
            if (_settings.SidebarEdge.Equals("left", StringComparison.OrdinalIgnoreCase))
            {
                leftLimit = work.Left + side;
            }
            else
            {
                rightLimit = work.Right - side;
            }
        }

        double bestX = Math.Max(work.Left, rightLimit - w);
        double bestY = work.Top + margin;
        bool found = false;

        for (double x = rightLimit - w; x >= leftLimit - 0.5 && !found; x -= w + gap)
        {
            double y = work.Top + margin;
            while (y + h <= work.Bottom - margin + 0.5)
            {
                var candidate = new Rect(x, y, w, h);
                Rect probe = candidate;
                probe.Inflate(-1, -1);
                Rect? blocker = null;
                foreach (Rect o in occupied)
                {
                    if (o.IntersectsWith(probe))
                    {
                        blocker = o;
                        break;
                    }
                }

                if (blocker is null)
                {
                    bestX = x;
                    bestY = y;
                    found = true;
                    break;
                }

                y = blocker.Value.Bottom + gap;
            }
        }

        if (!found)
        {
            // Screen is full: cascade from the top-left so the widget is still reachable.
            int index = occupied.Count;
            bestX = Math.Max(work.Left, Math.Min(work.Left + margin + index * 28, work.Right - w - margin));
            bestY = Math.Max(work.Top, Math.Min(work.Top + margin + index * 28, work.Bottom - h - margin));
        }

        p.X = Math.Round(bestX);
        p.Y = Math.Round(bestY);
        p.Width = w;
        p.Height = h;
        p.MonitorIndex = 0;
        p.AutoPlace = false;
    }

    /// <summary>
    /// Shows the instance in a floating window. If the window cannot be created
    /// the widget is docked into the sidebar instead — a widget must never just
    /// vanish (this was the "detach shows nothing" failure).
    /// </summary>
    private void HostFloatingOrDock(WidgetInstance instance, LayoutItem item)
    {
        try
        {
            HostDetached(instance, item.Placement);
        }
        catch (Exception ex)
        {
            Log.Error($"Could not create the floating window for '{instance.EffectiveTitle}' — docking it instead.", ex);
            item.Placement.Docked = true;
            ViewParenting.Release(instance.View);
            _sidebar.AddItem(instance);
        }
    }

    private void HostDetached(WidgetInstance instance, WidgetPlacement placement)
    {
        var spec = new WidgetPlacementSpec
        {
            X = placement.X,
            Y = placement.Y,
            Width = Math.Max(placement.Width, 120),
            Height = Math.Max(placement.Height, 90),
            Opacity = placement.Opacity,
            Theme = placement.Theme,
            AlwaysOnTop = placement.AlwaysOnTop,
            Layer = placement.Layer,
            ClickThrough = placement.ClickThrough,
            SnapToGrid = placement.SnapToGrid,
            MonitorIndex = placement.MonitorIndex,
            MonitorDeviceId = placement.MonitorDeviceId,
            MonitorOffsetX = placement.MonitorOffsetX,
            MonitorOffsetY = placement.MonitorOffsetY,
            HeightLimit = placement.HeightLimit,
        };

        Guid windowId = _widgetWindows.CreateWindow(instance, spec);
        _detachedWindows[instance.Id] = windowId;

        if (_widgetWindows.TryGetHost(windowId, out IWidgetHost? host))
        {
            // If the user closes the window (Alt+F4, tray "remove"), remove the
            // instance from the layout so persisted state stays in sync.
            host.Closed += OnHostClosed;
        }
    }

    private void OnHostClosed(object? sender, EventArgs e)
    {
        if (_tearingDown) return;
        if (sender is IWidgetHost h)
        {
            RemoveInstance(h.InstanceId);
        }
    }

    private WidgetInstance? FindInstance(Guid instanceId)
        => _instances.TryGetValue(instanceId, out WidgetInstance? instance) ? instance : null;

    private LayoutItem? FindItem(Guid instanceId)
        => _current.Items.FirstOrDefault(i => i.InstanceId == instanceId);

    public void BeginManualPlacement() => _initialArrangement.Clear();

    public bool IsLargeWidget(Guid id) => FindItem(id)?.Placement.LargeSize ?? false;

    public System.Windows.Controls.MenuItem BuildSizeMenu(Guid id)
    {
        var menu = new System.Windows.Controls.MenuItem { Header = "Size" };
        foreach (bool large in new[] { false, true })
        {
            var option = new System.Windows.Controls.MenuItem { Header = large ? "Large" : "Regular", IsCheckable = true };
            option.Click += (_, _) => SetWidgetLarge(id, large);
            menu.SubmenuOpened += (_, _) => option.IsChecked = IsLargeWidget(id) == large;
            menu.Items.Add(option);
        }
        return menu;
    }

    public void SetWidgetLarge(Guid id, bool large)
    {
        if (FindItem(id) is not { } item || FindInstance(id) is not { } instance || item.Placement.LargeSize == large) return;
        BeginManualPlacement();
        item.Placement.LargeSize = large;
        instance.View.LayoutTransform = new System.Windows.Media.ScaleTransform(large ? 1.35 : 1, large ? 1.35 : 1);
        if (_detachedWindows.TryGetValue(id, out var windowId) && _widgetWindows.TryGetHost(windowId, out var host) && host is WidgetWindow window)
        {
            window.Width *= large ? 1.35 : 1 / 1.35;
            window.ApplyContentSizing(large);
            window.CapturePlacement();
        }
        _sidebar.RefreshWidgetSizes();
        ScheduleSave();
    }

    public void SetDetachedAnchor(Guid id, string monitorId, double offsetX, double offsetY)
    {
        if (FindItem(id) is not { } item) return;
        item.Placement.MonitorDeviceId = monitorId;
        item.Placement.MonitorOffsetX = offsetX;
        item.Placement.MonitorOffsetY = offsetY;
        ScheduleSave();
    }

    private void ArrangeInitialWindows()
    {
        if (_arrangingInitial) return;
        _arrangingInitial = true;
        try
        {
        var items = _current.Items.Where(i => _initialArrangement.Contains(i.InstanceId) && !i.Placement.Docked && !i.Placement.Hidden).ToArray();
        // Native moves can finish a pending WPF measure. Read live heights again
        // after that pass instead of arranging from a stale model snapshot.
        for (int pass = 0; pass < 3; pass++)
        {
        foreach (var item in items)
            if (_detachedWindows.TryGetValue(item.InstanceId, out var id) &&
                _widgetWindows.TryGetHost(id, out var host) && host is WidgetWindow live && live.ActualHeight > 0)
                item.Placement.Height = live.ActualHeight;
        var plan = FloatingPlacement.ArrangeInitial(MonitorGeometry.PrimaryWorkAreaDip(), items.Select(i => new Size(i.Placement.Width, i.Placement.Height)).ToArray());
        for (int i = 0; i < items.Length; i++)
        {
            var p = items[i].Placement;
            p.X = plan[i].Bounds.X; p.Y = plan[i].Bounds.Y;
            p.Width = plan[i].Bounds.Width; p.Height = plan[i].Bounds.Height;
            p.HeightLimit = plan[i].HeightLimit;
            if (_detachedWindows.TryGetValue(items[i].InstanceId, out var windowId)) _widgetWindows.MoveTo(windowId, p.X, p.Y);
        }
        }
        if (items.Length > 0) ScheduleSave();
        }
        finally { _arrangingInitial = false; }
    }

    private void ClearRuntime()
    {
        // Rehosting and shutdown must never remove the persisted layout items.
        foreach (Guid id in _detachedWindows.Values.ToArray())
            if (_widgetWindows.TryGetHost(id, out var host)) host.Closed -= OnHostClosed;
        foreach (Guid id in _detachedWindows.Values.ToArray()) _widgetWindows.Close(id);
        _detachedWindows.Clear();
        _sidebar.ClearAll();
        foreach (var instance in _instances.Values) instance.ViewModel.Dispose();
        _instances.Clear();
    }
}
