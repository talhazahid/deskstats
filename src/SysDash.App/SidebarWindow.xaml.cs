using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using SysDash.Core.Diagnostics;
using SysDash.SDK.Session;
using SysDash.SDK.Widgets;
using SysDash.Theming;
using SysDash.Theming.Native;

namespace SysDash.App;

/// <summary>
/// Edge-docked sidebar shell. Hosts "docked" widget instances stacked in a
/// scroll list. Auto-hide collapses the shell to a thin edge strip and makes
/// it click-through; the strip is polled with the cursor position (DIPs).
/// </summary>
public sealed partial class SidebarWindow : Window
{
    private readonly AppSettings _settings;
    private readonly ISessionContext _session;
    private readonly IThemeService _theme;
    private readonly Dictionary<Guid, (Border Slot, WidgetInstance Instance)> _slots = new();
    private readonly DispatcherTimer _autoHideTimer;
    private double _expandedWidth;
    private WindowLayer _layer;
    private DateTime _lastCursorInside = DateTime.UtcNow;
    private bool _expanded = true;
    private bool _pinned;
    private bool _allowClose;

    private LayoutController? _controller;
    private Action<ContextMenu>? _appMenuBuilder;
    private readonly bool _transparent;
    private AdaptiveContrast? _contrast;

    private const double CollapsedWidth = 8;

    public SidebarWindow(AppSettings settings, ISessionContext session, IThemeService theme)
    {
        InitializeComponent();

        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _theme = theme ?? throw new ArgumentNullException(nameof(theme));

        _pinned = settings.SidebarPinned;
        _expandedWidth = Math.Clamp(settings.SidebarWidth, 240, 560);
        Width = _expandedWidth;
        ShowActivated = false;
        _layer = WindowLayerHelper.Parse(settings.SidebarLayer);
        Topmost = _layer == WindowLayer.Top;

        // Transparent sidebar ("Sidebar background" off): a layered window can only
        // be chosen before the window is created, so this applies on start-up.
        // 1/255 alpha keeps empty space clickable (right-click menu, wheel).
        _transparent = !settings.SidebarBackground;
        if (_transparent)
        {
            AllowsTransparency = true;
            Background = new SolidColorBrush(Color.FromArgb(1, 0, 0, 0));
        }

        ApplyLook();

        SourceInitialized += (_, _) => ApplyChrome();
        WindowLayerHelper.Attach(this, () => _layer);
        Loaded += (_, _) => SnapToEdge();
        SizeChanged += (_, _) => SnapToEdge();
        Closed += OnWindowClosed;

        // Screens added / removed / resolution changed → re-dock to the right place
        // (falls back to the main screen when the chosen one is unplugged).
        Microsoft.Win32.SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;

        // Wheel scrolls the sidebar on hover, even when it is not focused.
        HoverWheelScroll.Attach(this, Scroller);

        // Text stays readable whatever is behind see-through cards.
        _contrast = AdaptiveContrast.Attach(this);

        // Right-click on empty space → the tray menu (cards keep their own menu).
        var appMenu = new ContextMenu();
        appMenu.Opened += (_, _) =>
        {
            appMenu.Items.Clear();
            _appMenuBuilder?.Invoke(appMenu);
        };
        ContextMenu = appMenu;

        // Re-tint the native backdrop / title-bar mode when the theme changes.
        _theme.ThemeChanged += OnThemeChanged;

        _autoHideTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(150) };
        _autoHideTimer.Tick += (_, _) => PollAutoHide();

        if (settings.SidebarAutoHide && !_pinned)
        {
            _autoHideTimer.Start();
        }
    }

    public bool IsPinned => _pinned;

    /// <summary>
    /// Allows the singleton container to actually close. Called by the tray
    /// "Exit" path before <c>Application.Shutdown()</c>; every other close
    /// request (Alt+F4, system menu) becomes a Hide instead (P1).
    /// </summary>
    public void AllowClose() => _allowClose = true;

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!_allowClose)
        {
            // The sidebar is an optional container: closing it must never shut
            // down the tray app, nor orphan a closed window that can no longer
            // be shown again.
            e.Cancel = true;
            Hide();
        }

        base.OnClosing(e);
    }

    /// <summary>
    /// Wires the layout controller so card context menus can detach/edit/remove.
    /// Called by the composition root after both objects exist (the controller
    /// itself needs the sidebar, so this breaks the otherwise circular ctor).
    /// </summary>
    public void AttachController(LayoutController controller)
    {
        _controller = controller ?? throw new ArgumentNullException(nameof(controller));
    }

    /// <summary>
    /// Supplies the tray menu builder so right-clicking empty sidebar space shows
    /// exactly the same menu as the tray icon (built fresh on every open).
    /// </summary>
    public void SetAppMenuBuilder(Action<ContextMenu> build)
    {
        _appMenuBuilder = build ?? throw new ArgumentNullException(nameof(build));
    }

    /// <summary>Adds a docked widget instance to the shell.</summary>
    public void AddItem(WidgetInstance instance)
    {
        // The widget view draws its own card; the slot is only a spacer + context menu host.
        var slot = new Border
        {
            Margin = new Thickness(0, 0, 0, 10),
            Background = Brushes.Transparent,
            ContextMenu = BuildCardMenu(instance),
        };
        ViewParenting.Release(instance.View);
        slot.Child = instance.View;
        HostPanel.Children.Add(slot);
        _slots[instance.Id] = (slot, instance);
        RefreshWidgetSizes();
        instance.ViewModel.Activate(_session);
    }

    // The sidebar shares a width, so reserve space for the largest selected size.
    // Regular cards retain their normal scale within that width.
    public void RefreshWidgetSizes()
    {
        bool large = _slots.Keys.Any(id => _controller?.IsLargeWidget(id) == true);
        _expandedWidth = Math.Clamp(_settings.SidebarWidth, 240, 560) * (large ? 1.35 : 1);
        if (_expanded) Width = _expandedWidth;
        if (IsLoaded) Dispatcher.BeginInvoke(new Action(SnapToEdge), DispatcherPriority.Loaded);
    }

    private ContextMenu BuildCardMenu(WidgetInstance instance)
    {
        var menu = new ContextMenu();
        if (_controller is not null) menu.Items.Add(_controller.BuildSizeMenu(instance.Id));

        var detach = new MenuItem { Header = "Detach window" };
        detach.Click += (_, _) =>
        {
            if (_controller is not null)
            {
                _controller.DetachInstance(instance.Id);
            }
        };
        menu.Items.Add(detach);

        var settings = new MenuItem { Header = "Settings…" };
        settings.Click += (_, _) =>
        {
            if (_controller is not null)
            {
                _controller.OpenSettings(instance.Id);
            }
        };
        menu.Items.Add(settings);

        menu.Items.Add(new Separator());

        var remove = new MenuItem { Header = "Remove widget" };
        remove.Click += (_, _) =>
        {
            if (_controller is not null)
            {
                _controller.RemoveInstance(instance.Id);
            }
        };
        menu.Items.Add(remove);

        return menu;
    }

    public void RemoveItem(Guid instanceId)
    {
        if (_slots.Remove(instanceId, out (Border Slot, WidgetInstance Instance) entry))
        {
            // Release the widget View from the card: a re-homed view (sidebar →
            // floating window, or remove → re-add) must not stay the logical
            // child of a discarded slot, or WPF rejects the new parent.
            entry.Slot.Child = null;
            HostPanel.Children.Remove(entry.Slot);
            RefreshWidgetSizes();
        }
    }

    public void ClearAll()
    {
        foreach (Guid id in _slots.Keys.ToArray())
        {
            RemoveItem(id);
        }
    }

    /// <summary>Locks/unlocks the sidebar (disables auto-hide and click-through).</summary>
    public void SetPinned(bool pinned)
    {
        _pinned = pinned;
        _settings.SidebarPinned = pinned;

        if (pinned)
        {
            _autoHideTimer.Stop();
            SetExpanded(true);
        }
        else if (_settings.SidebarAutoHide)
        {
            _autoHideTimer.Start();
        }
    }

    /// <summary>
    /// Re-reads edge / width / layer / pin / auto-hide from settings and applies
    /// them immediately (no restart needed).
    /// </summary>
    public void ApplySettings()
    {
        _expandedWidth = Math.Clamp(_settings.SidebarWidth, 240, 560) *
            (_slots.Keys.Any(id => _controller?.IsLargeWidget(id) == true) ? 1.35 : 1);
        _layer = WindowLayerHelper.Parse(_settings.SidebarLayer);
        _pinned = _settings.SidebarPinned;

        BeginAnimation(WidthProperty, null);
        _expanded = true;
        Width = _expandedWidth;
        IsHitTestVisible = true;

        if (IsLoaded)
        {
            try
            {
                WindowEffects.SetClickThrough(this, false);
            }
            catch (InvalidOperationException)
            {
                // window already closed
            }
        }

        if (_settings.SidebarAutoHide && !_pinned)
        {
            _autoHideTimer.Start();
        }
        else
        {
            _autoHideTimer.Stop();
        }

        WindowLayerHelper.Apply(this, _layer);
        ApplyLook();
        Dispatcher.BeginInvoke(new Action(SnapToEdge), DispatcherPriority.Loaded);
    }

    /// <summary>Title visibility + card opacity / frame (live).</summary>
    private void ApplyLook()
    {
        HeaderText.Visibility = _settings.SidebarShowTitle ? Visibility.Visible : Visibility.Collapsed;
        // With the title hidden the widgets move up to the top edge.
        Scroller.Margin = _settings.SidebarShowTitle ? new Thickness(0) : new Thickness(0, 10, 0, 0);
        CardAppearance.Apply(this, _settings.SidebarCardOpacity, _settings.ShowCardFrame);
        _contrast?.Refresh();
    }

    private void ApplyChrome()
    {
        if (_transparent)
        {
            // No backdrop at all: only the widget cards are drawn.
            try
            {
                WindowEffects.SetToolWindow(this, true);
            }
            catch (Exception ex)
            {
                Log.Warn($"Sidebar chrome effect failed: {ex.Message}");
            }

            return;
        }

        try
        {
            WindowEffects.SetToolWindow(this, true);
            WindowEffects.ApplyImmersiveDarkMode(this, _theme.Current == ThemeKind.Dark);

            bool backdrop = WindowEffects.ApplySystemBackdrop(this, SystemBackdropStyle.Acrylic);
            if (!backdrop)
            {
                bool dark = _theme.Current == ThemeKind.Dark;
                WindowEffects.ApplyAcrylicFallback(this,
                    dark ? Color.FromRgb(16, 16, 22) : Color.FromRgb(236, 240, 246),
                    dark ? 0.78 : 0.88);
            }

            WindowEffects.ApplyCornerPreference(this, WindowCornerPreference.RoundSmall);
        }
        catch (Exception ex)
        {
            Log.Warn($"Sidebar chrome effect failed: {ex.Message}");
        }
    }

    private void SnapToEdge()
    {
        if (!IsLoaded)
        {
            return;
        }

        Rect work = MonitorGeometry.WorkAreaOfId(_settings.SidebarScreenId);
        Top = work.Top;
        Height = work.Height;

        if (IsRightEdge())
        {
            Left = work.Right - ActualWidth;
        }
        else
        {
            Left = work.Left;
        }
    }

    private bool IsRightEdge() => !_settings.SidebarEdge.Equals("left", StringComparison.OrdinalIgnoreCase);

    private void SetExpanded(bool expanded)
    {
        if (_expanded == expanded && Math.Abs(ActualWidth - (expanded ? _expandedWidth : CollapsedWidth)) < 1)
        {
            return;
        }

        _expanded = expanded;
        double target = expanded ? _expandedWidth : CollapsedWidth;

        BeginAnimation(WidthProperty, new DoubleAnimation(
            target,
            TimeSpan.FromMilliseconds(180))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut },
        });

        WindowEffects.SetClickThrough(this, !expanded && !_pinned);
        IsHitTestVisible = expanded;
    }

    private void PollAutoHide()
    {
        if (_pinned || !_settings.SidebarAutoHide || !IsVisible)
        {
            return;
        }

        Rect work = MonitorGeometry.WorkAreaOfId(_settings.SidebarScreenId);
        Point cursor = WindowEffects.GetCursorPositionDip();

        double probeWidth = _expanded ? _expandedWidth : CollapsedWidth;
        Rect hotZone = IsRightEdge()
            ? new Rect(work.Right - probeWidth - 20, work.Top, probeWidth + 40, work.Height)
            : new Rect(work.Left - 20, work.Top, probeWidth + 40, work.Height);

        if (hotZone.Contains(cursor))
        {
            _lastCursorInside = DateTime.UtcNow;
            if (!_expanded)
            {
                SetExpanded(true);
            }

            return;
        }

        if (_expanded && (DateTime.UtcNow - _lastCursorInside).TotalMilliseconds > 600)
        {
            SetExpanded(false);
        }
    }

    private void OnThemeChanged(object? sender, ThemeKind kind)
        => Dispatcher.BeginInvoke(new Action(() =>
        {
            if (IsLoaded)
            {
                ApplyChrome();
            }

            ApplyLook(); // card colours come from the new theme
        }));

    private void OnDisplaySettingsChanged(object? sender, EventArgs e)
        => Dispatcher.BeginInvoke(new Action(SnapToEdge), DispatcherPriority.Background);

    private void OnWindowClosed(object? sender, EventArgs e)
    {
        Microsoft.Win32.SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
        _theme.ThemeChanged -= OnThemeChanged;
        // The HWND is already destroyed here: touching it (EnsureHandle) throws.
        _autoHideTimer.Stop();
    }
}
