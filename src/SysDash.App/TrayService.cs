using System.Windows;
using System.Windows.Controls;
using Hardcodet.Wpf.TaskbarNotification;
using SysDash.Core.Diagnostics;
using SysDash.Layouts;
using SysDash.Layouts.Models;
using SysDash.Monitoring;
using SysDash.SDK.Widgets;
using SysDash.Theming;
using SysDash.Theming.Native;

namespace SysDash.App;

/// <summary>
/// Tray icon (Hardcodet.NotifyIcon.Wpf) with a fully rebuilt context menu —
/// the central controller (P1): add widgets (grouped by category; they float
/// by default), per-instance hide/show · dock/detach · settings · remove,
/// one-click reset of floating positions, layouts, theme, app settings, about
/// and exit. Left double-click toggles the sidebar when it has content. The
/// menu is rebuilt from live state on every open (hidden widgets included), so
/// nothing ever goes stale.
/// </summary>
public sealed class TrayService : IDisposable
{
    private readonly LayoutController _layout;
    private readonly IWidgetRegistry _registry;
    private readonly IThemeService _theme;
    private readonly SidebarWindow _sidebar;
    private readonly AppSettings _settings;
    private readonly AppSettingsService _settingsService;
    private readonly AutoStartService _autostart;
    private readonly IMetricsService? _metrics;
    private readonly TaskbarIcon _tray;

    public TrayService(
        LayoutController layout,
        IWidgetRegistry registry,
        IThemeService theme,
        SidebarWindow sidebar,
        AppSettings settings,
        AppSettingsService settingsService,
        AutoStartService autostart,
        IMetricsService? metrics = null)
    {
        _layout = layout ?? throw new ArgumentNullException(nameof(layout));
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _theme = theme ?? throw new ArgumentNullException(nameof(theme));
        _sidebar = sidebar ?? throw new ArgumentNullException(nameof(sidebar));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
        _autostart = autostart ?? throw new ArgumentNullException(nameof(autostart));
        _metrics = metrics;

        _tray = new TaskbarIcon
        {
            Icon = AppIcons.CreateTrayIcon(),
            ToolTipText = "DeskStats",
            ContextMenu = new ContextMenu(),
        };
        _tray.ContextMenu.Opened += OnMenuOpened;
        _tray.TrayMouseDoubleClick += (_, _) => ToggleSidebar();

        // Right-click on empty sidebar space shows this same menu.
        _sidebar.SetAppMenuBuilder(BuildMenu);
    }

    /// <summary>Shows a short balloon from the tray icon (used when nothing is visible at launch).</summary>
    public void ShowHint(string message)
    {
        try
        {
            _tray.ShowBalloonTip("DeskStats", message, BalloonIcon.Info);
        }
        catch (Exception ex)
        {
            Log.Debug($"Tray balloon failed: {ex.Message}");
        }
    }

    public void ToggleSidebar()
    {
        if (_sidebar.Visibility == Visibility.Visible)
        {
            _sidebar.Hide();
            return;
        }

        // P1: the sidebar is optional — only reveal it when it actually hosts
        // visible widgets; never materialize an empty full-height shell.
        if (_layout.LayoutItems.Any(i => i.Placement.Docked && !i.Placement.Hidden))
        {
            _sidebar.Show();
        }
    }

    private void OnMenuOpened(object? sender, RoutedEventArgs e)
    {
        if (sender is not ContextMenu menu || !ReferenceEquals(menu, _tray.ContextMenu))
        {
            return;
        }

        menu.Items.Clear();
        BuildMenu(menu);
    }

    private void BuildMenu(ContextMenu menu)
    {
        // ---- Add widget (grouped by category) -----------------------------
        // Each entry is a toggle: ✓ = on the desktop / in the sidebar; click to
        // hide it, click again to bring it back. "System" widgets (Recycle Bin,
        // Now Playing) are hidden from the menu for now.
        var addWidget = new MenuItem { Header = "Add widget" };
        foreach (string category in _registry.Categories)
        {
            if (HiddenCategories.Contains(category))
            {
                continue;
            }

            var categoryItem = new MenuItem { Header = Capitalize(category) };
            foreach (IWidget widget in _registry.ByCategory(category).OrderBy(w => w.DisplayName))
            {
                IWidget w = widget;
                var item = new MenuItem
                {
                    Header = w.DisplayName,
                    ToolTip = w.Description,
                    IsCheckable = true,
                    IsChecked = _layout.IsWidgetShown(w.Id),
                };
                item.Click += (_, _) => _layout.ToggleWidget(w.Id);
                categoryItem.Items.Add(item);
            }

            categoryItem.IsEnabled = categoryItem.Items.Count > 0;
            addWidget.Items.Add(categoryItem);
        }

        addWidget.Items.Add(new Separator());
        var showHardware = new MenuItem { Header = "Show all hardware widgets" };
        showHardware.Click += (_, _) => _layout.ShowAllHardwareWidgets();
        addWidget.Items.Add(showHardware);
        menu.Items.Add(addWidget);

        // ---- Layouts ------------------------------------------------------
        var layouts = new MenuItem { Header = "Layouts" };
        foreach (string name in _layout.LayoutNames)
        {
            string layoutName = name;
            var item = new MenuItem
            {
                Header = layoutName,
                IsCheckable = true,
                IsChecked = layoutName.Equals(_layout.CurrentLayoutName, StringComparison.OrdinalIgnoreCase),
            };
            item.Click += (_, _) => _layout.ActivateLayout(layoutName);
            layouts.Items.Add(item);
        }

        layouts.Items.Add(new Separator());
        var saveAs = new MenuItem { Header = "Save current as…" };
        saveAs.Click += (_, _) => SaveCurrentAs();
        layouts.Items.Add(saveAs);

        if (LayoutPresets.IsPreset(_layout.CurrentLayoutName))
        {
            var resetPreset = new MenuItem { Header = $"Reset '{_layout.CurrentLayoutName}' to preset" };
            resetPreset.Click += (_, _) => _layout.ResetToPreset();
            layouts.Items.Add(resetPreset);
        }

        menu.Items.Add(layouts);

        // ---- Display mode: all widgets floating, or all docked in the sidebar ----
        bool sidebarMode = _layout.IsSidebarMode;

        var toWidgets = new MenuItem { Header = "Switch to Widgets", IsCheckable = true, IsChecked = !sidebarMode };
        toWidgets.Click += (_, _) => _layout.SwitchToWidgets();
        menu.Items.Add(toWidgets);

        var toSidebar = new MenuItem { Header = "Switch to Sidebar", IsCheckable = true, IsChecked = sidebarMode };
        toSidebar.Click += (_, _) => _layout.SwitchToSidebar();
        menu.Items.Add(toSidebar);

        menu.Items.Add(new Separator());

        // ---- Theme ---------------------------------------------------------
        var theme = new MenuItem { Header = "Theme" };
        var dark = new MenuItem { Header = "Dark", IsCheckable = true, IsChecked = _theme.Current == ThemeKind.Dark };
        dark.Click += (_, _) => _layout.SetTheme(ThemeKind.Dark);
        theme.Items.Add(dark);
        var light = new MenuItem { Header = "Light", IsCheckable = true, IsChecked = _theme.Current == ThemeKind.Light };
        light.Click += (_, _) => _layout.SetTheme(ThemeKind.Light);
        theme.Items.Add(light);
        menu.Items.Add(theme);

        // ---- Widgets (instances; hidden ones stay reachable) ----------------
        var instances = new MenuItem { Header = "Widgets" };
        IReadOnlyList<LayoutItem> items = _layout.LayoutItems;
        foreach (LayoutItem entry in items)
        {
            LayoutItem it = entry;
            string title = it.TitleOverride
                ?? _registry.Find(it.WidgetId)?.DisplayName
                ?? it.WidgetId;
            var sub = new MenuItem { Header = title, ToolTip = it.WidgetId };

            if (it.Placement.Hidden)
            {
                var show = new MenuItem { Header = "Show" };
                show.Click += (_, _) => _layout.ShowInstance(it.InstanceId);
                sub.Items.Add(show);
            }
            else
            {
                var hide = new MenuItem { Header = "Hide" };
                hide.Click += (_, _) => _layout.HideInstance(it.InstanceId);
                sub.Items.Add(hide);

                if (it.Placement.Docked)
                {
                    var detach = new MenuItem { Header = "Detach window" };
                    detach.Click += (_, _) => _layout.DetachInstance(it.InstanceId);
                    sub.Items.Add(detach);
                }
                else
                {
                    var dock = new MenuItem { Header = "Dock to sidebar" };
                    dock.Click += (_, _) => _layout.DockInstance(it.InstanceId);
                    sub.Items.Add(dock);

                    var clickThrough = new MenuItem
                    {
                        Header = "Click-through",
                        IsCheckable = true,
                        IsChecked = _layout.IsClickThrough(it.InstanceId),
                    };
                    clickThrough.Click += (_, _) => _layout.SetClickThrough(it.InstanceId, clickThrough.IsChecked);
                    sub.Items.Add(clickThrough);
                }

                var settingsItem = new MenuItem { Header = "Settings…" };
                settingsItem.Click += (_, _) => _layout.OpenSettings(it.InstanceId);
                sub.Items.Add(settingsItem);
            }

            sub.Items.Add(new Separator());

            var remove = new MenuItem { Header = "Remove" };
            remove.Click += (_, _) => _layout.RemoveInstance(it.InstanceId);
            sub.Items.Add(remove);

            instances.Items.Add(sub);
        }

        instances.IsEnabled = items.Count > 0;
        menu.Items.Add(instances);

        var reset = new MenuItem { Header = "Reset widget positions" };
        reset.IsEnabled = items.Any(i => !i.Placement.Docked && !i.Placement.Hidden);
        reset.Click += (_, _) => _layout.ResetFloatPositions();
        menu.Items.Add(reset);

        menu.Items.Add(new Separator());

        // ---- Sensor driver (CPU temperature / clock / power) -----------------
        if (SensorDriver.MissingReason is not null)
        {
            if (SensorDriver.IsElevated)
            {
                var installDriver = new MenuItem { Header = "Install sensor driver (CPU temperature)…" };
                installDriver.Click += async (_, _) =>
                {
                    ShowHint("Installing the PawnIO sensor driver… DeskStats restarts when it is done.");
                    if (!await SensorDriver.InstallPawnIOAsync())
                    {
                        ShowHint(PawnIOInstaller.LastMessage);
                    }
                };
                menu.Items.Add(installDriver);
            }
            else
            {
                var elevate = new MenuItem { Header = "Restart as administrator (CPU sensors)" };
                elevate.Click += (_, _) =>
                {
                    try
                    {
                        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(
                            Environment.ProcessPath ?? "DeskStats.exe", "--restarted")
                        {
                            UseShellExecute = true,
                            Verb = "runas",
                        });
                        _layout.Dispose();
                        _sidebar.AllowClose();
                        Application.Current.Shutdown();
                    }
                    catch (Exception ex)
                    {
                        Log.Warn($"Elevated restart cancelled: {ex.Message}");
                    }
                };
                menu.Items.Add(elevate);
            }

            menu.Items.Add(new Separator());
        }

        var report = new MenuItem { Header = "Hardware report…", ToolTip = "What DeskStats detected on this PC (for checking or support)." };
        report.Click += (_, _) =>
        {
            string path = SysDash.Monitoring.Hardware.HardwareReport.FilePath;
            if (!System.IO.File.Exists(path))
            {
                ShowHint("The hardware report is created a few seconds after DeskStats starts.");
                return;
            }

            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                Log.Warn($"Could not open hardware report: {ex.Message}");
            }
        };
        menu.Items.Add(report);

        var appSettings = new MenuItem { Header = "App settings…" };
        appSettings.Click += (_, _) => OpenAppSettings();
        menu.Items.Add(appSettings);

        var about = new MenuItem { Header = "About DeskStats" };
        var updates = new MenuItem { Header = "Check for updates…" };
        updates.Click += async (_, _) => { if (Application.Current is App app) await app.CheckUpdatesAsync(true); };
        menu.Items.Add(updates);
        about.Click += (_, _) => ShowAbout();
        menu.Items.Add(about);

        var exit = new MenuItem { Header = "Exit" };
        exit.Click += (_, _) =>
        {
            _layout.Dispose(); // Save before WPF closes windows and raises host.Closed.
            _sidebar.AllowClose();
            Application.Current.Shutdown();
        };
        menu.Items.Add(exit);
    }

    private void SaveCurrentAs()
    {
        var dialog = new NameDialog("Save layout as…");
        ApplyOwner(dialog);
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        if (!_layout.SaveCurrentAs(dialog.ResultName))
        {
            MessageBox.Show(
                "That name is reserved for a built-in preset. Pick another name.",
                "DeskStats",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private void OpenAppSettings()
    {
        var window = new AppSettingsWindow(
            _settings, _settingsService, _layout, _sidebar, _autostart, _metrics);
        ApplyOwner(window);
        window.ShowDialog();
    }

    private void ShowAbout()
    {
        var window = new AboutWindow();
        ApplyOwner(window);
        window.ShowDialog();
    }

    /// <summary>
    /// Owns dialogs to the sidebar only while it is actually visible; with a
    /// floating-first setup (P1) dialogs otherwise appear unowned instead of
    /// centering on a never-shown window.
    /// </summary>
    private void ApplyOwner(Window window)
    {
        // Floating widgets can be always-on-top; keep dialogs above them.
        window.Topmost = true;

        if (_sidebar.IsVisible)
        {
            window.Owner = _sidebar;
        }
    }

    /// <summary>Widget categories not offered in the menu (their widgets still load from old layouts).</summary>
    private static readonly HashSet<string> HiddenCategories = new(StringComparer.OrdinalIgnoreCase)
    {
        WidgetCategories.System,
    };

    private static string Capitalize(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return value;
        }

        return char.ToUpperInvariant(value[0]) + value[1..];
    }

    public void Dispose()
    {
        _tray.ContextMenu.Opened -= OnMenuOpened;
        _tray.Dispose();
    }
}
