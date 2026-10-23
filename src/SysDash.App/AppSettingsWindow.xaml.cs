using System.Windows;
using System.Windows.Controls;
using SysDash.Monitoring;
using SysDash.Theming;
using SysDash.Theming.Native;

namespace SysDash.App;

/// <summary>
/// App-level preferences editor. Values that the running services can apply
/// live (theme, poll interval, autostart, pin) are applied immediately; sidebar
/// geometry and sensor selection take effect on the next start (noted in the UI).
/// </summary>
public partial class AppSettingsWindow : Window
{
    private readonly AppSettings _settings;
    private readonly AppSettingsService _settingsService;
    private readonly LayoutController _layout;
    private readonly SidebarWindow _sidebar;
    private readonly AutoStartService _autostart;
    private readonly IMetricsService? _metrics;
    private readonly List<string> _screenIds = new();
    private readonly List<(MonitorDescriptor Monitor, string Label)> _screens = new();
    private bool _loading;

    public AppSettingsWindow(
        AppSettings settings,
        AppSettingsService settingsService,
        LayoutController layout,
        SidebarWindow sidebar,
        AutoStartService autostart,
        IMetricsService? metrics = null)
    {
        InitializeComponent();

        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
        _layout = layout ?? throw new ArgumentNullException(nameof(layout));
        _sidebar = sidebar ?? throw new ArgumentNullException(nameof(sidebar));
        _autostart = autostart ?? throw new ArgumentNullException(nameof(autostart));
        _metrics = metrics;

        LoadValues();
    }

    private void LoadValues()
    {
        PollBox.Text = _settings.PollIntervalMs.ToString();
        GpuCheck.IsChecked = _settings.LoadGpu;
        DrivesCheck.IsChecked = _settings.LoadDrives;
        NetworkCheck.IsChecked = _settings.LoadNetwork;

        SelectByLabel(ThemeBox, _settings.Theme.Equals("light", StringComparison.OrdinalIgnoreCase) ? "Light" : "Dark");
        // Screens named by position + model + resolution (no numbers: Windows has
        // no API for the numbers its Settings page shows, they differ per PC).
        _loading = true;
        ScreenBox.Items.Clear();
        _screenIds.Clear();
        _screens.Clear();
        ScreenBox.Items.Add("Main screen (automatic)");
        _screenIds.Add(string.Empty);
        foreach ((MonitorDescriptor monitor, string label) in MonitorGeometry.DescribeMonitors())
        {
            ScreenBox.Items.Add(label);
            _screenIds.Add(monitor.DeviceId);
            _screens.Add((monitor, label));
        }

        int selected = _screenIds.FindIndex(id => id.Length > 0 &&
            string.Equals(id, _settings.SidebarScreenId, StringComparison.OrdinalIgnoreCase));
        ScreenBox.SelectedIndex = selected >= 0 ? selected : 0;
        _loading = false;

        SelectByLabel(EdgeBox, _settings.SidebarEdge.Equals("left", StringComparison.OrdinalIgnoreCase) ? "Left" : "Right");
        CardFrameCheck.IsChecked = _settings.ShowCardFrame;
        SidebarOpacitySlider.Value = Math.Clamp(Math.Round(_settings.SidebarCardOpacity * 100), 0, 100);
        SidebarOpacityText.Text = $"{SidebarOpacitySlider.Value:0}%";
        SidebarTitleCheck.IsChecked = _settings.SidebarShowTitle;
        SidebarBackgroundCheck.IsChecked = _settings.SidebarBackground;
        AutoHideCheck.IsChecked = _settings.SidebarAutoHide;
        PinCheck.IsChecked = _settings.SidebarPinned;
        AutostartCheck.IsChecked = _settings.StartWithWindows;
        AutomaticUpdatesCheck.IsChecked = _settings.AutomaticUpdates;
    }

    private async void OnCheckUpdates(object sender, RoutedEventArgs e)
    {
        if (Application.Current is App app) await app.CheckUpdatesAsync(true);
    }

    private void OnSave(object sender, RoutedEventArgs e)
    {
        try
        {
            int poll = int.Parse(PollBox.Text.Trim());
            poll = Math.Clamp(poll, 200, 60_000);
            _settings.PollIntervalMs = poll;

            _settings.LoadGpu = GpuCheck.IsChecked == true;
            _settings.LoadDrives = DrivesCheck.IsChecked == true;
            _settings.LoadNetwork = NetworkCheck.IsChecked == true;

            string? themeLabel = SelectedText(ThemeBox);
            _settings.Theme = themeLabel?.StartsWith("light", StringComparison.OrdinalIgnoreCase) == true
                ? "light"
                : "dark";

            int screenPick = ScreenBox.SelectedIndex;
            _settings.SidebarScreenId = screenPick > 0 && screenPick < _screenIds.Count ? _screenIds[screenPick] : string.Empty;

            string? edge = SelectedText(EdgeBox);
            _settings.SidebarEdge = edge?.StartsWith("left", StringComparison.OrdinalIgnoreCase) == true
                ? "left"
                : "right";


            _settings.ShowCardFrame = CardFrameCheck.IsChecked == true;
            _settings.SidebarCardOpacity = Math.Clamp(SidebarOpacitySlider.Value / 100.0, 0, 1);
            _settings.SidebarShowTitle = SidebarTitleCheck.IsChecked == true;
            bool backgroundChanged = _settings.SidebarBackground != (SidebarBackgroundCheck.IsChecked == true);
            _settings.SidebarBackground = SidebarBackgroundCheck.IsChecked == true;
            _settings.SidebarAutoHide = AutoHideCheck.IsChecked == true;
            _settings.SidebarPinned = PinCheck.IsChecked == true;
            _settings.StartWithWindows = AutostartCheck.IsChecked == true;
            _settings.AutomaticUpdates = AutomaticUpdatesCheck.IsChecked == true;

            _settingsService.Save();

            // Live-applied effects (everything else loads on the next start).
            _metrics?.ChangePollInterval(_settings.PollIntervalMs);
            _sidebar.ApplySettings();
            _layout.SetTheme(_settings.Theme.Equals("light", StringComparison.OrdinalIgnoreCase)
                ? ThemeKind.Light
                : ThemeKind.Dark);
            _autostart.Apply();

            // Card frame / opacity apply live to every floating widget too.
            foreach (WidgetWindow widget in Application.Current.Windows.OfType<WidgetWindow>())
            {
                widget.RefreshCardAppearance();
            }

            DialogResult = true;
            Close();

            if (backgroundChanged && MessageBox.Show(
                    "The sidebar background changes after a restart. Restart DeskStats now?",
                    "DeskStats", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
            {
                _sidebar.AllowClose();
                ((App)Application.Current).RestartApp();
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Could not save settings:\n{ex.Message}", "DeskStats");
        }
    }

    /// <summary>Shows each screen's name on that screen (like Windows' Identify, but always matching our list).</summary>
    private void OnIdentifyScreens(object sender, RoutedEventArgs e) => ScreenIdentifier.Show(_screens);

    /// <summary>Picking a screen briefly shows its name on it, so the choice is obvious.</summary>
    private void OnScreenPicked(object sender, SelectionChangedEventArgs e)
    {
        int i = ScreenBox.SelectedIndex - 1;
        if (!_loading && i >= 0 && i < _screens.Count)
        {
            ScreenIdentifier.Show(new[] { _screens[i] });
        }
    }

    private void OnSidebarOpacityChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (SidebarOpacityText is not null)
        {
            SidebarOpacityText.Text = $"{e.NewValue:0}%";
        }
    }

    private void OnCancel(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private static void SelectByLabel(ComboBox combo, string label)
    {
        foreach (object item in combo.Items)
        {
            if (item is ComboBoxItem cbi && string.Equals(cbi.Content?.ToString(), label, StringComparison.OrdinalIgnoreCase))
            {
                combo.SelectedItem = item;
                return;
            }
        }
    }

    private static string? SelectedText(ComboBox combo)
        => (combo.SelectedItem as ComboBoxItem)?.Content?.ToString();
}
