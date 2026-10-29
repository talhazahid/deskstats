using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using SysDash.Core.Diagnostics;
using SysDash.SDK.Hosting;
using SysDash.SDK.Session;
using SysDash.SDK.Widgets;
using SysDash.SDK.WindowManagement;
using SysDash.Theming;
using SysDash.Theming.Controls;
using SysDash.Theming.Native;

namespace SysDash.App;

/// <summary>
/// Detached (floating) window for one widget instance. Built entirely in code:
/// a glassed card with an invisible top drag strip and a bottom-right resize
/// grip. Right-clicking the strip opens the per-widget context menu. Placement
/// changes are pushed back to the LayoutController so they persist in the
/// active layout.
/// </summary>
public sealed class WidgetWindow : Window, IWidgetHost
{
    private readonly WidgetInstance _instance;
    private readonly LayoutController _layout;
    private readonly ISessionContext _session;
    private readonly IThemeService _theme;
    private readonly AppSettings _settings;
    private double _cardOpacity = 1.0;
    private string? _themeOverride; // "dark" / "light" / null = follow the app
    private ResourceDictionary? _ownTheme;
    private readonly AdaptiveContrast _contrast;
    private Border? _card;
    private WindowLayer _layer;
    private readonly bool _startClickThrough;
    private bool _dragResizing;
    private bool _resizeMode;
    private Point _resizeStart;
    private double _startWidth;
    private double _startLeft;
    private (double Min, double Max) _limits = (FallbackMinWidth, 520);
    private Border? _leftHandle;
    private Border? _rightHandle;
    private Border? _resizeBadge;
    private TextBlock? _resizeHint;
    private bool _restoringPlacement = true;
    private bool _missingSavedMonitor;
    private string? _savedMonitorDeviceId;
    private double _savedOffsetX, _savedOffsetY;
    private readonly DispatcherTimer _placementTimer;
    [StructLayout(LayoutKind.Sequential)]
    private struct PixelRect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)]
    private struct PixelPoint { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)]
    private struct MinMaxInfo
    {
        public PixelPoint Reserved, MaxSize, MaxPosition, MinTrackSize, MaxTrackSize;
    }
    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hwnd, out PixelRect rect);
    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam);

    private event EventHandler? _hostClosed;

    public WidgetWindow(
        WidgetInstance instance,
        WidgetPlacementSpec spec,
        LayoutController layout,
        ISessionContext session,
        IThemeService theme,
        AppSettings settings)
    {
        _instance = instance ?? throw new ArgumentNullException(nameof(instance));
        _layout = layout ?? throw new ArgumentNullException(nameof(layout));
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _theme = theme ?? throw new ArgumentNullException(nameof(theme));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _savedMonitorDeviceId = spec.MonitorDeviceId;
        _savedOffsetX = spec.MonitorOffsetX;
        _savedOffsetY = spec.MonitorOffsetY;
        _placementTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(150) };
        _placementTimer.Tick += (_, _) => { _placementTimer.Stop(); CapturePlacement(); };

        Title = instance.EffectiveTitle;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        // Fully transparent layered window: only the widget's own rounded card is
        // visible (no dark rectangle around it), Rainmeter-style.
        AllowsTransparency = true;
        ShowActivated = false;
        _startClickThrough = spec.ClickThrough;
        _layer = WindowLayerHelper.Parse(spec.Layer, spec.AlwaysOnTop ? WindowLayer.Top : WindowLayer.Normal);
        Topmost = _layer == WindowLayer.Top;
        // "Opacity" fades only the card background — text and graphs stay solid.
        _cardOpacity = Math.Clamp(spec.Opacity, 0.0, 1.0);

        Width = Math.Clamp(spec.Width, 220, 1200);
        Height = Math.Clamp(spec.Height, 120, 1600);
        PlaceAt(spec);
        // Saved layout limits must never turn a widget into a scrollable viewport.
        MaxHeight = double.PositiveInfinity;

        // Height always follows the widget's content, so detailed widgets are
        // never clipped; the user only chooses the width (right-click → Resize width).
        SizeToContent = SizeToContent.Height;

        Background = Brushes.Transparent;

        Content = BuildChrome();
        if (_layout.IsLargeWidget(_instance.Id)) ApplyContentSizing(true);

        // Leave resize mode with Esc or by clicking somewhere else.
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape && _resizeMode)
            {
                SetResizeMode(false);
            }
        };
        Deactivated += (_, _) =>
        {
            if (_resizeMode && !_dragResizing)
            {
                SetResizeMode(false);
            }
        };
        _contrast = AdaptiveContrast.Attach(this);
        ApplyOwnTheme(spec.Theme);
        RefreshCardAppearance();
        _theme.ThemeChanged += OnThemeChanged;
        base.Closed += (_, _) => _theme.ThemeChanged -= OnThemeChanged;
        base.Closed += (_, _) => _clickThroughWatch?.Stop();

        SourceInitialized += (_, _) =>
        {
            HwndSource.FromHwnd(new WindowInteropHelper(this).Handle)?.AddHook(ContentHeightWindowProc);
            // The HWND received its first size limits before SourceInitialized.
            // Refresh WPF's cached native limits after installing our hook.
            IntPtr buffer = Marshal.AllocHGlobal(Marshal.SizeOf<MinMaxInfo>());
            try
            {
                var limits = new MinMaxInfo
                {
                    MaxSize = new PixelPoint { X = 32767, Y = 32767 },
                    MaxTrackSize = new PixelPoint { X = 32767, Y = 32767 },
                };
                Marshal.StructureToPtr(limits, buffer, false);
                SendMessage(new WindowInteropHelper(this).Handle, 0x0024, IntPtr.Zero, buffer);
            }
            finally { Marshal.FreeHGlobal(buffer); }
            ApplyChrome();
        };

        // Monitor plugged / unplugged / resolution changed: keep the widget reachable.
        Microsoft.Win32.SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
        base.Closed += (_, _) => Microsoft.Win32.SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
        WindowLayerHelper.Attach(this, () => _layer);
        Loaded += (_, _) =>
        {
            RestoreMonitorAnchor(spec);
            _restoringPlacement = false;
            _instance.ViewModel.Activate(_session);

            // Height follows the content; remember the real size so auto-layout
            // of other widgets does not overlap this one.
            _layout.SetDetachedSize(_instance.Id, Width, ActualHeight);
            CapturePlacement();
        };
        LocationChanged += (_, _) => { if (!_restoringPlacement) { _placementTimer.Stop(); _placementTimer.Start(); } };
        SizeChanged += (_, _) =>
        {
            if (!_restoringPlacement && ActualWidth > 0 && ActualHeight > 0)
                _layout.SetDetachedSize(_instance.Id, ActualWidth, ActualHeight);
        };
        base.Closed += (_, _) =>
        {
            _placementTimer.Stop();
            // Release the widget View before the window is gone so it can be
            // re-homed (e.g. "Dock to sidebar") without WPF complaining that it
            // is still a logical child of this window's card.
            if (_card is not null)
            {
                _card.Child = null;
                _card = null;
            }

            _instance.ViewModel.Deactivate();
            _hostClosed?.Invoke(this, EventArgs.Empty);
        };
    }

    // ---- IWidgetHost -------------------------------------------------------

    public Guid InstanceId => _instance.Id;

    public string HostName => $"window:{_instance.Id:N}";

    public bool IsDetached => true;

    public bool HostVisible
    {
        get => Visibility == Visibility.Visible;
        set => Visibility = value ? Visibility.Visible : Visibility.Hidden;
    }

    event EventHandler? IWidgetHost.Closed
    {
        add => _hostClosed += value;
        remove => _hostClosed -= value;
    }

    // Window.Show/Hide already satisfy IWidgetHost.Show/Hide implicitly.

    public void Dispose()
    {
        if (IsLoaded)
        {
            // Closing raises Closed, which deactivates the view model. Close()
            // is safe here even when called reentrantly from RemoveInstance.
            Close();
        }
        else
        {
            _instance.ViewModel.Deactivate();
        }
    }

    // ---- placement API used by WidgetWindowService ------------------------

    public WindowLayer Layer => _layer;

    public void SetAlwaysOnTop(bool alwaysOnTop)
        => SetLayer(alwaysOnTop ? WindowLayer.Top : WindowLayer.Normal);

    public void SetLayer(WindowLayer layer)
    {
        _layer = layer;
        WindowLayerHelper.Apply(this, layer);
        _layout.SetDetachedFlags(_instance.Id, layer: WindowLayerHelper.ToKey(layer));
    }

    public void SetClickThrough(bool clickThrough)
    {
        _clickThrough = clickThrough;
        _tempInteractive = false;
        WindowEffects.SetClickThrough(this, clickThrough);
        _layout.SetDetachedFlags(_instance.Id, clickThrough: clickThrough);
        UpdateClickThroughWatch();
    }

    // ---- click-through rescue --------------------------------------------
    //
    // A click-through widget ignores the mouse by design, so on its own it can
    // never be right-clicked again. While click-through is on, the cursor is
    // watched (cheap 150 ms poll, only while needed):
    //   • hovering the widget shows a small "Turn off click-through" pill
    //     (its own clickable mini window) at the widget's top-right corner;
    //   • holding Ctrl over the widget makes it clickable for as long as Ctrl
    //     is held — drag it, right-click for the full menu, etc.

    private bool _clickThrough;
    private bool _tempInteractive;
    private bool _menuOpen;
    private DispatcherTimer? _clickThroughWatch;
    private Window? _unlockPill;
    private DateTime _hoverSince = DateTime.MaxValue;

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int key);

    private const int VkControl = 0x11;

    private void UpdateClickThroughWatch()
    {
        if (_clickThrough)
        {
            _clickThroughWatch ??= CreateWatch();
            _clickThroughWatch.Start();
        }
        else
        {
            _clickThroughWatch?.Stop();
            HideUnlockPill();
        }
    }

    private DispatcherTimer CreateWatch()
    {
        var timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(150) };
        timer.Tick += (_, _) => WatchClickThrough();
        return timer;
    }

    private void WatchClickThrough()
    {
        if (!_clickThrough || !IsVisible)
        {
            HideUnlockPill();
            return;
        }

        Point cursor = WindowEffects.GetCursorPositionDip();
        bool overWidget = new Rect(Left, Top, ActualWidth, ActualHeight).Contains(cursor);
        bool overPill = _unlockPill is { IsVisible: true } pill &&
                        new Rect(pill.Left, pill.Top, pill.ActualWidth, pill.ActualHeight).Contains(cursor);
        bool ctrl = (GetAsyncKeyState(VkControl) & 0x8000) != 0;

        // Ctrl held over the widget → temporarily interactive.
        if (overWidget && ctrl && !_tempInteractive)
        {
            _tempInteractive = true;
            WindowEffects.SetClickThrough(this, false);
        }
        else if (_tempInteractive && !_menuOpen && !_dragResizing && (!overWidget || !ctrl))
        {
            _tempInteractive = false;
            WindowEffects.SetClickThrough(this, true);
        }

        // Hover (≈0.4 s) → show the unlock pill; leave → hide it.
        if (overWidget || overPill)
        {
            if (_hoverSince == DateTime.MaxValue)
            {
                _hoverSince = DateTime.UtcNow;
            }

            if ((DateTime.UtcNow - _hoverSince).TotalMilliseconds > 400)
            {
                ShowUnlockPill();
            }
        }
        else
        {
            _hoverSince = DateTime.MaxValue;
            HideUnlockPill();
        }
    }

    private void ShowUnlockPill()
    {
        if (_unlockPill is null)
        {
            var text = new TextBlock
            {
                Text = "Turn off click-through",
                FontSize = 11.5,
                FontWeight = FontWeights.SemiBold,
                Foreground = Brushes.White,
                VerticalAlignment = VerticalAlignment.Center,
            };
            var hint = new TextBlock
            {
                Text = "or hold Ctrl",
                FontSize = 10.5,
                Foreground = new SolidColorBrush(Color.FromArgb(0xB0, 0xFF, 0xFF, 0xFF)),
                Margin = new Thickness(8, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
            };
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            row.Children.Add(text);
            row.Children.Add(hint);

            var pillBorder = new Border
            {
                CornerRadius = new CornerRadius(11),
                Padding = new Thickness(11, 5, 11, 5),
                Cursor = Cursors.Hand,
                Child = row,
            };
            pillBorder.SetResourceReference(Border.BackgroundProperty, "AccentBrush");

            _unlockPill = new Window
            {
                WindowStyle = WindowStyle.None,
                AllowsTransparency = true,
                Background = Brushes.Transparent,
                ResizeMode = ResizeMode.NoResize,
                ShowInTaskbar = false,
                ShowActivated = false,
                Topmost = true,
                SizeToContent = SizeToContent.WidthAndHeight,
                Content = pillBorder,
            };
            pillBorder.MouseLeftButtonUp += (_, e) =>
            {
                e.Handled = true;
                SetClickThrough(false);
            };
            pillBorder.MouseRightButtonUp += (_, e) =>
            {
                e.Handled = true;
                OpenMenu();
            };
            Closed += (_, _) => _unlockPill?.Close();
        }

        if (!_unlockPill.IsVisible)
        {
            _unlockPill.Show();
        }

        // Top-right corner of the card, inside the widget.
        _unlockPill.Left = Left + Math.Max(0, ActualWidth - _unlockPill.ActualWidth - 18);
        _unlockPill.Top = Top + 16;
    }

    private void HideUnlockPill()
    {
        if (_unlockPill is { IsVisible: true })
        {
            _unlockPill.Hide();
        }
    }

    private void OpenMenu()
    {
        ContextMenu menu = BuildContextMenu();
        _menuOpen = true;
        menu.Closed += (_, _) => _menuOpen = false;
        menu.IsOpen = true;
    }

    /// <summary>Card background opacity (0–1). Text, rings and graphs are not faded.</summary>
    public void SetWindowOpacity(double opacity)
    {
        _cardOpacity = Math.Clamp(opacity, 0.0, 1.0);
        RefreshCardAppearance();
        _layout.SetDetachedFlags(_instance.Id, opacity: _cardOpacity);
    }

    /// <summary>Re-applies background opacity + card frame (after settings or theme changes).</summary>
    public void RefreshCardAppearance()
    {
        CardAppearance.Apply(this, _cardOpacity, _settings.ShowCardFrame);
        _contrast?.Refresh();
    }

    /// <summary>This widget's own theme: "dark", "light", or null/"" = same as the app.</summary>
    public void SetWidgetTheme(string? theme)
    {
        ApplyOwnTheme(theme);
        RefreshCardAppearance();
        _layout.SetDetachedTheme(_instance.Id, _themeOverride);
    }

    private void ApplyOwnTheme(string? theme)
    {
        ThemeKind? kind = ThemeManager.ParseOverride(theme);
        _themeOverride = kind switch
        {
            ThemeKind.Dark => "dark",
            ThemeKind.Light => "light",
            _ => null,
        };

        if (_ownTheme is not null)
        {
            Resources.MergedDictionaries.Remove(_ownTheme);
            _ownTheme = null;
        }

        if (kind is { } k)
        {
            // Window-level dictionary: every DynamicResource inside this widget
            // resolves here first, so only this widget changes theme.
            _ownTheme = ThemeManager.LoadDictionary(k);
            Resources.MergedDictionaries.Add(_ownTheme);
        }
    }

    private void OnThemeChanged(object? sender, ThemeKind kind)
        => Dispatcher.BeginInvoke(new Action(RefreshCardAppearance));

    /// <summary>
    /// Moves the window to the given DIP position (tray "Reset widget
    /// positions"). Persisting the new position is the caller's job — the
    /// drag/snap path pushes positions through the LayoutController as well.
    /// </summary>
    public void MoveTo(double x, double y)
    {
        _missingSavedMonitor = false;
        Left = x;
        Top = y;
        CapturePlacement(userMove: true);
    }

    public void ApplyContentSizing(bool large)
    {
        MaxHeight = double.PositiveInfinity;
        SizeToContent = SizeToContent.Height;
        InvalidateMeasure();
    }

    // ---- chrome ------------------------------------------------------------

    private FrameworkElement BuildChrome()
    {
        var root = new Grid { Background = Brushes.Transparent };

        // The widget view draws its own glass card; this host only adds breathing
        // room around it (a second card here caused double padding + clipping).
        // Soft shadow under the card. It is a separate element (not an Effect on
        // the card itself) so the widget's text keeps crisp ClearType rendering.
        var shadow = new Border
        {
            Margin = new Thickness(10),
            CornerRadius = new CornerRadius(18),
        };
        shadow.SetResourceReference(Border.BackgroundProperty, CardAppearance.CardBackgroundKey);
        // Theme-specific shadow (soft + cool in Light, deeper in Dark); follows theme switches live.
        shadow.SetResourceReference(UIElement.EffectProperty, "CardShadow");
        root.Children.Add(shadow);

        var card = new Border
        {
            Margin = new Thickness(10),
            Background = Brushes.Transparent,
        };
        ViewParenting.Release(_instance.View);
        card.Child = _instance.View;
        _card = card;
        root.Children.Add(card);

        // Drag from anywhere on the widget (buttons inside the widget mark the
        // event handled, so they keep working); right-click anywhere for the menu.
        root.PreviewMouseLeftButtonDown += OnStripDrag;
        root.MouseRightButtonUp += (_, args) =>
        {
            OpenMenu();
            args.Handled = true;
        };

        // Resize mode (right-click → "Resize width"): handles on the left and right
        // edges + a hint badge; hidden the rest of the time so the widget stays clean.
        _leftHandle = BuildEdgeHandle(HorizontalAlignment.Left);
        _rightHandle = BuildEdgeHandle(HorizontalAlignment.Right);
        root.Children.Add(_leftHandle);
        root.Children.Add(_rightHandle);

        _resizeHint = new TextBlock
        {
            FontSize = 11.5,
            FontWeight = FontWeights.SemiBold,
            Foreground = Brushes.White,
        };
        _resizeBadge = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(0xE0, 0x16, 0x16, 0x1A)),
            CornerRadius = new CornerRadius(9),
            Padding = new Thickness(10, 4, 10, 4),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 2, 0, 0),
            IsHitTestVisible = false,
            Visibility = Visibility.Collapsed,
            Child = _resizeHint,
        };
        root.Children.Add(_resizeBadge);

        return root;
    }

    private void OnStripDrag(object sender, MouseButtonEventArgs e)
    {
        // Listen during preview while preserving interactive controls in the view.
        for (DependencyObject? node = e.OriginalSource as DependencyObject;
             node is not null && !ReferenceEquals(node, sender);
             node = node is Visual ? VisualTreeHelper.GetParent(node) : null)
        {
            if (node is System.Windows.Controls.Primitives.ButtonBase or
                System.Windows.Controls.Primitives.ScrollBar or TextBox or Slider)
                return;
        }
        if (ClickThroughEnabled)
        {
            return;
        }

        try
        {
            _layout.BeginManualPlacement();
            _missingSavedMonitor = false;
            e.Handled = true;
            DragMove();
            double snappedX = SnapAxis(Left);
            double snappedY = SnapAxis(Top);
            Left = snappedX;
            Top = snappedY;

            // Remember which screen it was dropped on (Windows display number; 0 = unknown).
            Point center = new(snappedX + ActualWidth / 2, snappedY + Math.Min(ActualHeight, 80) / 2);
            int monitor = MonitorGeometry.MonitorAt(center)?.Number ?? 0;
            _layout.SetDetachedPosition(_instance.Id, snappedX, snappedY, monitor);
            CapturePlacement(userMove: true);
        }
        catch (InvalidOperationException)
        {
            // DragMove can throw when the button is already captured; ignore.
        }
    }

    // ---- resize mode -------------------------------------------------------

    private const double FallbackMinWidth = 260;
    private const double MaxColumnWidth = 340; // UX cap: columns wider than this read poorly

    private Border BuildEdgeHandle(HorizontalAlignment side)
    {
        var pill = new Border
        {
            Width = 5,
            Height = 46,
            CornerRadius = new CornerRadius(2.5),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        pill.SetResourceReference(Border.BackgroundProperty, "AccentBrush");

        var handle = new Border
        {
            Width = 14,
            HorizontalAlignment = side,
            VerticalAlignment = VerticalAlignment.Stretch,
            Margin = new Thickness(side == HorizontalAlignment.Left ? 3 : 0, 14, side == HorizontalAlignment.Right ? 3 : 0, 14),
            Background = Brushes.Transparent, // whole strip is grabbable
            Cursor = Cursors.SizeWE,
            Visibility = Visibility.Collapsed,
            Child = pill,
            Tag = side,
        };
        handle.MouseLeftButtonDown += OnHandleDown;
        handle.MouseMove += OnHandleMove;
        handle.MouseLeftButtonUp += OnHandleUp;
        return handle;
    }

    /// <summary>Shows / hides the edge handles. Ends with Esc, a click elsewhere, or the menu.</summary>
    private void SetResizeMode(bool on)
    {
        _resizeMode = on;
        Visibility v = on ? Visibility.Visible : Visibility.Collapsed;
        if (_leftHandle is not null)
        {
            _leftHandle.Visibility = v;
        }

        if (_rightHandle is not null)
        {
            _rightHandle.Visibility = v;
        }

        if (_resizeBadge is not null)
        {
            _resizeBadge.Visibility = v;
        }

        if (on)
        {
            UpdateResizeHint();
            Activate(); // receive Esc
        }
    }

    private (double Min, double Max) WidthLimits()
    {
        ResponsiveColumns? panel = FindPanel(this);
        double chrome = panel is { ActualWidth: > 0 } ? Math.Max(0, ActualWidth - panel.ActualWidth) : 40;
        if (panel is null)
        {
            return (FallbackMinWidth, 520);
        }

        int columns = panel.UsefulColumns;
        double min = panel.MinColumnWidth + chrome;
        double max = columns * MaxColumnWidth + (columns - 1) * panel.ColumnGap + chrome;

        Rect work = MonitorGeometry.WorkAreaFor(new Point(Left + 20, Top + 20), 0);
        max = Math.Min(max, Math.Max(min, work.Width - 20));
        return (Math.Round(min), Math.Round(Math.Max(min, max)));
    }

    private void UpdateResizeHint()
    {
        if (_resizeHint is null)
        {
            return;
        }

        int columns = FindPanel(this)?.ColumnCount ?? 1;
        _resizeHint.Text = $"{columns} column{(columns == 1 ? string.Empty : "s")} · drag the edges · Esc to finish";
    }

    private void OnHandleDown(object sender, MouseButtonEventArgs e)
    {
        _layout.BeginManualPlacement();
        _missingSavedMonitor = false;
        _dragResizing = true;
        _resizeStart = WindowEffects.GetCursorPositionDip();
        _startWidth = ActualWidth;
        _startLeft = Left;
        _limits = WidthLimits();
        (sender as UIElement)?.CaptureMouse();
        e.Handled = true;
    }

    private void OnHandleMove(object sender, MouseEventArgs e)
    {
        if (!_dragResizing || sender is not FrameworkElement { Tag: HorizontalAlignment side })
        {
            return;
        }

        double dx = WindowEffects.GetCursorPositionDip().X - _resizeStart.X;
        double width = side == HorizontalAlignment.Right ? _startWidth + dx : _startWidth - dx;
        width = Math.Round(Math.Clamp(width, _limits.Min, _limits.Max));

        if (side == HorizontalAlignment.Left)
        {
            Left = _startLeft + (_startWidth - width); // right edge stays put
        }

        Width = width;
        UpdateResizeHint();
        e.Handled = true;
    }

    private void OnHandleUp(object sender, MouseButtonEventArgs e)
    {
        if (!_dragResizing)
        {
            return;
        }

        _dragResizing = false;
        (sender as UIElement)?.ReleaseMouseCapture();
        _layout.SetDetachedSize(_instance.Id, Width, ActualHeight);
        _layout.SetDetachedPosition(_instance.Id, Left, Top);
        CapturePlacement();
        Dispatcher.BeginInvoke(new Action(UpdateResizeHint), System.Windows.Threading.DispatcherPriority.Loaded);
        e.Handled = true;
    }

    private static ResponsiveColumns? FindPanel(DependencyObject root)
    {
        int count = VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < count; i++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(root, i);
            if (child is ResponsiveColumns panel)
            {
                return panel;
            }

            if (FindPanel(child) is { } found)
            {
                return found;
            }
        }

        return null;
    }

    private double SnapAxis(double value)
    {
        if (!_layout.IsGridSnapEnabled(_instance.Id))
        {
            return value;
        }

        const int step = 6;
        return Math.Round(value / step) * step;
    }

    private void OnDisplaySettingsChanged(object? sender, EventArgs e)
    {
        Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(RestoreSavedDisplayPosition));
    }

    public void RestoreSavedDisplayPosition()
    {
        if (!IsLoaded || string.IsNullOrEmpty(_savedMonitorDeviceId)) return;
        if (Mouse.LeftButton == MouseButtonState.Pressed) return;
        var saved = MonitorGeometry.FindById(_savedMonitorDeviceId);
        _missingSavedMonitor = saved is null;
        // A monitor may disappear temporarily during screen timeout. Keep its
        // anchor intact until it returns; never save Windows' temporary relocation.
        if (saved is null) return;
        _restoringPlacement = true;
        try
        {
            Point pixels = FloatingPlacement.RestorePixels(saved, _savedOffsetX, _savedOffsetY, ActualWidth, ActualHeight);
            if (GetWindowRect(new WindowInteropHelper(this).Handle, out PixelRect current) &&
                Math.Abs(current.Left - pixels.X) <= 1 && Math.Abs(current.Top - pixels.Y) <= 1) return;
            SetWindowPos(new WindowInteropHelper(this).Handle, IntPtr.Zero, (int)Math.Round(pixels.X), (int)Math.Round(pixels.Y), 0, 0, 0x0015);
        }
        finally { _restoringPlacement = false; }
    }

    private bool ClickThroughEnabled => WindowEffects.IsClickThrough(this);

    private ContextMenu BuildContextMenu()
    {
        var menu = new ContextMenu { PlacementTarget = this };

        var layerMenu = new MenuItem { Header = "Layer" };
        void AddLayer(string header, WindowLayer layer)
        {
            var item = new MenuItem { Header = header, IsCheckable = true, IsChecked = _layer == layer };
            item.Click += (_, _) => SetLayer(layer);
            layerMenu.Items.Add(item);
        }

        AddLayer("Always on top", WindowLayer.Top);
        AddLayer("Normal", WindowLayer.Normal);
        AddLayer("On desktop (behind windows)", WindowLayer.Bottom);
        menu.Items.Add(layerMenu);

        var clickThrough = new MenuItem { Header = "Click-through", IsCheckable = true, IsChecked = _clickThrough };
        clickThrough.Click += (_, _) => SetClickThrough(clickThrough.IsChecked);
        menu.Items.Add(clickThrough);

        var opacity = new MenuItem { Header = "Background opacity" };
        foreach (double level in new[] { 0.0, 0.2, 0.4, 0.6, 0.8, 1.0 })
        {
            int value = (int)Math.Round(level * 100);
            var item = new MenuItem
            {
                Header = value == 0 ? "0% (text only)" : $"{value}%",
                IsCheckable = true,
                IsChecked = Math.Abs(_cardOpacity - level) < 0.01,
            };
            item.Click += (_, _) => SetWindowOpacity(level);
            opacity.Items.Add(item);
        }
        menu.Items.Add(opacity);

        var themeMenu = new MenuItem { Header = "Theme" };
        void AddTheme(string header, string? key)
        {
            var item = new MenuItem
            {
                Header = header,
                IsCheckable = true,
                IsChecked = string.Equals(_themeOverride, key, StringComparison.OrdinalIgnoreCase),
            };
            item.Click += (_, _) => SetWidgetTheme(key);
            themeMenu.Items.Add(item);
        }

        AddTheme("Same as app", null);
        AddTheme("Dark", "dark");
        AddTheme("Light", "light");
        themeMenu.Items.Add(new Separator());
        var frame = new MenuItem { Header = "Card frame (all widgets)", IsCheckable = true, IsChecked = _layout.ShowCardFrame };
        frame.Click += (_, _) => _layout.SetCardFrame(frame.IsChecked);
        themeMenu.Items.Add(frame);
        menu.Items.Add(themeMenu);
        menu.Items.Add(_layout.BuildSizeMenu(_instance.Id));
        menu.Items.Add(new Separator());

        var resize = new MenuItem { Header = _resizeMode ? "Done resizing" : "Resize width" };
        resize.Click += (_, _) => SetResizeMode(!_resizeMode);
        menu.Items.Add(resize);

        var settings = new MenuItem { Header = "Settings…" };
        settings.Click += (_, _) => _layout.OpenSettings(_instance.Id);
        menu.Items.Add(settings);

        var dock = new MenuItem { Header = "Dock to sidebar" };
        dock.Click += (_, _) => _layout.DockInstance(_instance.Id);
        menu.Items.Add(dock);

        var remove = new MenuItem { Header = "Remove widget" };
        remove.Click += (_, _) => _layout.RemoveInstance(_instance.Id);
        menu.Items.Add(remove);

        return menu;
    }

    private void PlaceAt(WidgetPlacementSpec spec)
    {
        if (string.IsNullOrEmpty(spec.MonitorDeviceId))
        {
            Rect oldBounds = new(spec.X, spec.Y, Width, Height);
            bool reachable = MonitorGeometry.EnumerateMonitors().Any(m =>
            {
                Rect visible = Rect.Intersect(oldBounds, m.BoundsDip);
                return !visible.IsEmpty && visible.Width >= 48 && visible.Height >= 48;
            });
            if (reachable)
            {
                Left = spec.X;
                Top = spec.Y;
                return; // Migrate a reachable legacy position without snapping it elsewhere.
            }
        }
        // Use the screen the widget was saved on (any monitor, not just the
        // primary); if that screen is gone, bring it back to the main one.
        Rect work = MonitorGeometry.FindById(spec.MonitorDeviceId)?.WorkAreaDip ??
            MonitorGeometry.WorkAreaFor(new Point(spec.X + 20, spec.Y + 20), spec.MonitorIndex);

        Rect rect = MonitorGeometry.ClampToWorkArea(
            new Rect(spec.X, spec.Y, Width, Height), work);
        Left = rect.X;
        Top = rect.Y;
    }

    private void RestoreMonitorAnchor(WidgetPlacementSpec spec)
    {
        if (string.IsNullOrEmpty(spec.MonitorDeviceId)) return; // Legacy coordinates migrate on capture.
        var monitors = MonitorGeometry.EnumerateMonitors();
        var saved = monitors.FirstOrDefault(m => string.Equals(m.DeviceId, spec.MonitorDeviceId, StringComparison.OrdinalIgnoreCase));
        _missingSavedMonitor = saved is null;
        var monitor = saved ?? monitors.FirstOrDefault(m => m.IsPrimary) ?? monitors.FirstOrDefault();
        if (monitor is null) return;
        Point pixels = FloatingPlacement.RestorePixels(monitor, spec.MonitorOffsetX, spec.MonitorOffsetY, Width, ActualHeight);
        SetWindowPos(new WindowInteropHelper(this).Handle, IntPtr.Zero, (int)Math.Round(pixels.X), (int)Math.Round(pixels.Y), 0, 0, 0x0015);
    }

    public void CapturePlacement(bool userMove = false)
    {
        if (_restoringPlacement || !IsLoaded) return;
        if (!userMove && !string.IsNullOrEmpty(_savedMonitorDeviceId))
        {
            RestoreSavedDisplayPosition();
            _layout.SetDetachedSize(_instance.Id, ActualWidth, ActualHeight);
            return;
        }
        if (_missingSavedMonitor && !userMove) return;
        if (!GetWindowRect(new WindowInteropHelper(this).Handle, out PixelRect pixels)) return;
        var monitors = MonitorGeometry.EnumerateMonitors();
        Point center = new((pixels.Left + pixels.Right) / 2.0, pixels.Top + Math.Min(40, pixels.Bottom - pixels.Top) / 2.0);
        var monitor = monitors.FirstOrDefault(m =>
            new Rect(m.BoundsDip.X * m.DpiX / 96.0, m.BoundsDip.Y * m.DpiY / 96.0, m.PixelWidth, m.PixelHeight).Contains(center));
        if (monitor is null) return;
        Rect work = FloatingPlacement.PixelWorkArea(monitor);
        _missingSavedMonitor = false;
        _savedMonitorDeviceId = monitor.DeviceId;
        _savedOffsetX = (pixels.Left - work.Left) * 96.0 / monitor.DpiX;
        _savedOffsetY = (pixels.Top - work.Top) * 96.0 / monitor.DpiY;
        _layout.SetDetachedPosition(_instance.Id, Left, Top, monitor.Number);
        _layout.SetDetachedAnchor(_instance.Id, monitor.DeviceId, _savedOffsetX, _savedOffsetY);
        _layout.SetDetachedSize(_instance.Id, ActualWidth, ActualHeight);
    }

    private IntPtr ContentHeightWindowProc(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message == 0x0024) // WM_GETMINMAXINFO: Windows otherwise caps even auto-sized windows to the screen.
        {
            var limits = Marshal.PtrToStructure<MinMaxInfo>(lParam);
            limits.MaxSize.Y = 32767;
            limits.MaxTrackSize.Y = 32767;
            Marshal.StructureToPtr(limits, lParam, false);
        }
        return IntPtr.Zero;
    }

    private void ApplyChrome()
    {
        try
        {
            // Transparent layered window: no DWM backdrop (it would paint a
            // rectangle behind the rounded card). Just keep it out of Alt+Tab.
            WindowEffects.SetToolWindow(this, true);

            // Restore a saved click-through state (toggle it off from the tray).
            if (_startClickThrough)
            {
                _clickThrough = true;
                WindowEffects.SetClickThrough(this, true);
                UpdateClickThroughWatch();
            }
        }
        catch (Exception ex)
        {
            Log.Warn($"Widget window chrome effect failed: {ex.Message}");
        }
    }
}
