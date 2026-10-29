using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Threading;
using System.Windows.Interop;
using System.Runtime.InteropServices;
using Microsoft.Extensions.DependencyInjection;
using SysDash.App;
using SysDash.Core.Models;
using SysDash.Core.Threading;
using SysDash.Layouts;
using SysDash.SDK.Session;
using SysDash.SDK.Widgets;
using SysDash.SDK.WindowManagement;
using SysDash.Theming;
using SysDash.Theming.Native;

internal static class WindowPersistenceChecks
{
    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
    public static bool Run(string output)
    {
        string root = Path.Combine(output, "window-lifecycle", Guid.NewGuid().ToString("N"));
        var checks = new Dictionary<string, bool>();
        Dictionary<Guid, (string? Screen, double X, double Y)> expected;
        using (var services = Build(root))
        {
            var controller = services.GetRequiredService<LayoutController>();
            controller.ApplyActiveLayout();
            Pump();
            // Simulate content growing after the first sensor snapshot.
            foreach (var instance in controller.Instances) instance.View.MinHeight = 900;
            Pump();
            var windows = Application.Current.Windows.OfType<WidgetWindow>().ToArray();
            File.WriteAllText(Path.Combine(output, "initial-window-bounds.json"), JsonSerializer.Serialize(windows.Select(w => new { w.Title, w.Left, w.Top, w.ActualWidth, w.ActualHeight, MaxHeight = double.IsPositiveInfinity(w.MaxHeight) ? (double?)null : w.MaxHeight }), new JsonSerializerOptions { WriteIndented = true }));
            checks["CreatesAllDefaultWidgets"] = windows.Length == LayoutController.DefaultWidgetIds.Length;
            checks["LiveContentGrowthDoesNotOverlap"] = !windows.SelectMany((a, i) => windows.Skip(i + 1).Select(b =>
                new Rect(a.Left, a.Top, a.ActualWidth, a.ActualHeight).IntersectsWith(new Rect(b.Left, b.Top, b.ActualWidth, b.ActualHeight)))).Any(x => x);
            controller.BeginManualPlacement();
            var monitors = MonitorGeometry.EnumerateMonitors();
            for (int i = 0; i < windows.Length; i++)
            {
                var monitor = monitors[i % monitors.Count];
                Point pixels = FloatingPlacement.RestorePixels(monitor, 45 + i * 17, 85 + i * 13, windows[i].ActualWidth, windows[i].ActualHeight);
                SetWindowPos(new WindowInteropHelper(windows[i]).Handle, IntPtr.Zero, (int)pixels.X, (int)pixels.Y, 0, 0, 0x0015);
                windows[i].CapturePlacement(userMove: true);
            }
            // Exit immediately, before the normal save debounce can finish.
            controller.Dispose();
            var disk = new LayoutService(Path.Combine(root, "layouts"));
            disk.EnsureDefaults();
            expected = disk.ActiveLayout.Items.ToDictionary(i => i.InstanceId, i => (i.Placement.MonitorDeviceId, i.Placement.MonitorOffsetX, i.Placement.MonitorOffsetY));
            checks["ExitKeepsAllWidgetEntries"] = expected.Count == LayoutController.DefaultWidgetIds.Length;
            checks["ExitStoresStableMonitorIdentities"] = expected.Values.All(x => !string.IsNullOrEmpty(x.Screen));
            checks["UsesConnectedScreens"] = expected.Values.Select(x => x.Screen).Distinct().Count() == Math.Min(windows.Length, monitors.Count);
            CloseSidebar(services);
        }
        using (var services = Build(root))
        {
            var controller = services.GetRequiredService<LayoutController>();
            controller.ApplyActiveLayout();
            Pump();
            controller.Dispose();
            var disk = new LayoutService(Path.Combine(root, "layouts"));
            disk.EnsureDefaults();
            var items = disk.ActiveLayout.Items;
            checks["RestartRestoresMonitorsAndRepairsOverlaps"] = items.Count == expected.Count && items.All(i => expected.TryGetValue(i.InstanceId, out var p) &&
                p.Screen == i.Placement.MonitorDeviceId) && Application.Current.Windows.OfType<WidgetWindow>().All(a =>
                    Application.Current.Windows.OfType<WidgetWindow>().Where(b => a != b).All(b =>
                        !new Rect(a.Left, a.Top, a.ActualWidth, a.ActualHeight).IntersectsWith(new Rect(b.Left, b.Top, b.ActualWidth, b.ActualHeight))));
            CloseSidebar(services);
        }
        using (var services = Build(Path.Combine(root, "docking")))
        {
            var controller = services.GetRequiredService<LayoutController>();
            controller.ApplyActiveLayout();
            Pump();
            checks["DockOperationSucceeds"] = controller.DockInstance(controller.Instances.First().Id);
            Pump();
            var sidebar = services.GetRequiredService<SidebarWindow>();
            var windows = Application.Current.Windows.OfType<WidgetWindow>().ToArray();
            // Deliberately reproduce a floating widget occupying the sidebar.
            windows[0].MoveTo(sidebar.Left, sidebar.Top + 24);
            var relocate = typeof(LayoutController).GetMethod("MoveFloatingWidgetsClearOfSidebar", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
            relocate.Invoke(controller, null);
            Pump();
            Rect reserved = new(sidebar.Left, sidebar.Top, sidebar.ActualWidth, sidebar.ActualHeight);
            checks["FloatingWidgetsClearSidebar"] = windows.All(w => !reserved.IntersectsWith(new Rect(w.Left, w.Top, w.ActualWidth, w.ActualHeight)));
            var retained = controller.Instances.First(i => i.WidgetId == "cpu").Id;
            controller.HideInstance(controller.Instances.First(i => i.WidgetId == "gpu").Id);
            controller.RemoveInstance(controller.Instances.First(i => i.WidgetId == "ram").Id);
            controller.ShowAllHardwareWidgets();
            controller.ShowAllHardwareWidgets();
            Pump();
            checks["RestoreHardwareShowsDefaultsWithoutDuplicates"] = controller.Instances.Count() == LayoutController.DefaultWidgetIds.Length &&
                LayoutController.DefaultWidgetIds.All(controller.IsWidgetShown) && controller.Instances.Any(i => i.Id == retained);
            checks["NormalWidgetsHaveNoHeightCapOrScrollHost"] = Application.Current.Windows.OfType<WidgetWindow>().All(w =>
                double.IsPositiveInfinity(w.MaxHeight) && w.SizeToContent == SizeToContent.Height &&
                !HasScrollViewer(w));
            var tall = controller.Instances.First(i => Application.Current.Windows.OfType<WidgetWindow>().Any(w => w.InstanceId == i.Id));
            double previousMinHeight = tall.View.MinHeight;
            double requiredHeight = MonitorGeometry.PrimaryWorkAreaDip().Height + 300;
            tall.View.MinHeight = requiredHeight;
            Pump();
            var tallWindow = Application.Current.Windows.OfType<WidgetWindow>().Single(w => w.InstanceId == tall.Id);
            checks["ContentTallerThanScreenIsNotClipped"] = tallWindow.ActualHeight >= requiredHeight && tall.View.ActualHeight >= requiredHeight;
            var floatingNow = Application.Current.Windows.OfType<WidgetWindow>().ToArray();
            // Existing saved/manual positions must reflow when sensor data grows.
            controller.BeginManualPlacement();
            foreach (var window in floatingNow) window.MoveTo(tallWindow.Left, tallWindow.Top);
            tall.View.MinHeight = requiredHeight + 100;
            Pump();
            checks["SavedManualLayoutReflowsWithSpacing"] = floatingNow.All(a => floatingNow.Where(b => b != a).All(b =>
            {
                var padded = new Rect(a.Left, a.Top, a.ActualWidth, a.ActualHeight);
                padded.Inflate(FloatingPlacement.Gap / 2 - 0.1, FloatingPlacement.Gap / 2 - 0.1);
                var other = new Rect(b.Left, b.Top, b.ActualWidth, b.ActualHeight);
                other.Inflate(FloatingPlacement.Gap / 2 - 0.1, FloatingPlacement.Gap / 2 - 0.1);
                return !padded.IntersectsWith(other);
            }));
            File.WriteAllText(Path.Combine(output, "reflow-bounds.json"), JsonSerializer.Serialize(floatingNow.Select(w => new { w.Title, w.Left, w.Top, w.ActualWidth, w.ActualHeight }), new JsonSerializerOptions { WriteIndented = true }));
            File.WriteAllText(Path.Combine(output, "tall-widget-bounds.json"), JsonSerializer.Serialize(new { requiredHeight, WindowHeight = tallWindow.ActualHeight, ContentHeight = tall.View.ActualHeight }));
            tall.View.MinHeight = previousMinHeight;
            Pump();
            foreach (var instance in controller.Instances) controller.SetWidgetLarge(instance.Id, true);
            Pump();
            checks["EveryHardwareWidgetSupportsLargeSize"] = controller.Instances.All(i => controller.IsLargeWidget(i.Id) && Math.Abs(i.View.LayoutTransform.Value.M11 - 1.35) < 0.001);
            checks["LargeWidgetsHaveNoHeightCap"] = Application.Current.Windows.OfType<WidgetWindow>().All(w => double.IsPositiveInfinity(w.MaxHeight) && w.SizeToContent == SizeToContent.Height);
            foreach (var instance in controller.Instances) controller.SetWidgetLarge(instance.Id, false);
            Pump();
            checks["ReturningToNormalNeverRestoresScrolling"] = Application.Current.Windows.OfType<WidgetWindow>().All(w =>
                double.IsPositiveInfinity(w.MaxHeight) && !HasScrollViewer(w));
            foreach (var instance in controller.Instances) controller.SetWidgetLarge(instance.Id, true);
            Pump();
            foreach (var window in Application.Current.Windows.OfType<WidgetWindow>())
            {
                var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap((int)Math.Ceiling(window.ActualWidth), (int)Math.Ceiling(window.ActualHeight), 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
                bitmap.Render(window);
                var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
                encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
                using var file = File.Create(Path.Combine(output, "large-" + window.Title + ".png"));
                encoder.Save(file);
            }
            controller.Dispose();
            var savedSizes = new LayoutService(Path.Combine(root, "docking", "layouts"));
            savedSizes.EnsureDefaults();
            checks["LargeSizePersistsForEveryWidget"] = savedSizes.ActiveLayout.Items.All(i => i.Placement.LargeSize);
            CloseSidebar(services);
        }
        string stateRoot = Path.Combine(root, "state-roundtrip");
        using (var services = Build(stateRoot))
        {
            var controller = services.GetRequiredService<LayoutController>();
            controller.ApplyActiveLayout(); Pump();
            controller.SetWidgetLarge(controller.Instances.First(i => i.WidgetId == "cpu").Id, true);
            controller.HideInstance(controller.Instances.First(i => i.WidgetId == "gpu").Id);
            controller.RemoveInstance(controller.Instances.First(i => i.WidgetId == "ram").Id);
            var window = Application.Current.Windows.OfType<WidgetWindow>().First();
            var location = new Point(window.Left, window.Top);
            // Windows can move a window without a user drag when displays change.
            SetWindowPos(new WindowInteropHelper(window).Handle, IntPtr.Zero, 0, 0, 0, 0, 0x0015);
            window.CapturePlacement(); Pump();
            checks["AutomaticRelocationRestoresUserAnchor"] = Math.Abs(window.Left - location.X) <= 1 && Math.Abs(window.Top - location.Y) <= 1;
            controller.Dispose(); CloseSidebar(services);
        }
        using (var services = Build(stateRoot))
        {
            var controller = services.GetRequiredService<LayoutController>();
            controller.ApplyActiveLayout(); Pump();
            checks["RestartKeepsHiddenRemovedAndLargeState"] = controller.Instances.Count() == LayoutController.DefaultWidgetIds.Length - 2 &&
                !controller.IsWidgetShown("gpu") && !controller.IsWidgetShown("ram") &&
                controller.IsLargeWidget(controller.Instances.First(i => i.WidgetId == "cpu").Id);
            controller.ClearAll();
            // Remove hidden items too to test an intentionally empty saved layout.
            var layouts = services.GetRequiredService<ILayoutService>();
            controller.Dispose();
            var empty = layouts.ActiveLayout; empty.Items.Clear(); layouts.SaveActive(empty);
            CloseSidebar(services);
        }
        using (var services = Build(stateRoot))
        {
            var controller = services.GetRequiredService<LayoutController>();
            controller.ApplyActiveLayout(); Pump();
            checks["SavedEmptyLayoutDoesNotRepopulateDefaults"] = !controller.Instances.Any();
            var preferences = services.GetRequiredService<AppSettingsService>();
            var settingsWindow = new AppSettingsWindow(preferences.Settings, preferences, controller,
                services.GetRequiredService<SidebarWindow>(), new AutoStartService(preferences.Settings, preferences));
            settingsWindow.Show(); Pump();
            var settingsBitmap = new System.Windows.Media.Imaging.RenderTargetBitmap((int)Math.Ceiling(settingsWindow.ActualWidth), (int)Math.Ceiling(settingsWindow.ActualHeight), 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
            settingsBitmap.Render(settingsWindow);
            var settingsEncoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
            settingsEncoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(settingsBitmap));
            using (var file = File.Create(Path.Combine(output, "settings-updates.png"))) settingsEncoder.Save(file);
            settingsWindow.Close();
            controller.Dispose(); CloseSidebar(services);
        }
        var installationLayouts = new LayoutService(Path.Combine(root, "installation"));
        installationLayouts.EnsureDefaults();
        var installationSettings = new AppSettings();
        var customLayout = installationLayouts.ActiveLayout;
        customLayout.Items.Clear();
        installationLayouts.SaveActive(customLayout);
        checks["FreshInstallRestoresHardwareWidgets"] = InstallationState.Apply("install-one", installationSettings, installationLayouts) &&
            LayoutController.DefaultWidgetIds.All(id => installationLayouts.ActiveLayout.Items.Any(i => i.WidgetId == id));
        customLayout = installationLayouts.ActiveLayout;
        customLayout.Items.RemoveAll(i => i.WidgetId != "cpu");
        customLayout.Items[0].Placement.LargeSize = true;
        customLayout.Items[0].Placement.MonitorDeviceId = "retained-screen";
        installationLayouts.SaveActive(customLayout);
        checks["UpgradePreservesCustomizedState"] = !InstallationState.Apply("install-one", installationSettings, installationLayouts) &&
            installationLayouts.ActiveLayout.Items.Count == 1 && installationLayouts.ActiveLayout.Items[0].Placement.LargeSize &&
            installationLayouts.ActiveLayout.Items[0].Placement.MonitorDeviceId == "retained-screen";
        checks["ReinstallGetsFreshDefaults"] = InstallationState.Apply("install-two", installationSettings, installationLayouts) && installationLayouts.ActiveLayout.Items.Count == LayoutController.DefaultWidgetIds.Length;
        string hash = new string('A', 64);
        checks["UpdateAcceptsVersionedRelease"] = UpdateService.Parse(System.Text.Json.JsonSerializer.Serialize(new { version = "1.0.14", url = "https://talhazahid.github.io/deskstats/downloads/DeskStats-Setup-1.0.14.exe", sha256 = hash })).Version == "1.0.14";
        checks["UpdateRejectsUntrustedHost"] = RejectUpdate("https://example.com/deskstats/downloads/DeskStats-Setup-1.0.14.exe", hash);
        checks["UpdateRejectsUnversionedAsset"] = RejectUpdate("https://talhazahid.github.io/deskstats/downloads/latest.exe", hash);
        checks["UpdateRejectsInvalidChecksum"] = RejectUpdate("https://talhazahid.github.io/deskstats/downloads/DeskStats-Setup-1.0.14.exe", "bad");
        File.WriteAllText(Path.Combine(output, "window-persistence-checks.json"), JsonSerializer.Serialize(checks, new JsonSerializerOptions { WriteIndented = true }));
        return checks.Values.All(x => x);
    }

    private static bool HasScrollViewer(DependencyObject root)
    {
        if (root is System.Windows.Controls.ScrollViewer) return true;
        for (int i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(root); i++)
            if (HasScrollViewer(System.Windows.Media.VisualTreeHelper.GetChild(root, i))) return true;
        return false;
    }

    private static bool RejectUpdate(string url, string hash)
    {
        try { UpdateService.Parse(JsonSerializer.Serialize(new { version = "1.0.14", url, sha256 = hash })); return false; }
        catch (InvalidDataException) { return true; }
    }

    private static ServiceProvider Build(string root)
    {
        var settings = new AppSettingsService(root);
        var layouts = new LayoutService(Path.Combine(root, "layouts"));
        layouts.EnsureDefaults();
        var services = new ServiceCollection();
        services.AddSingleton(settings);
        services.AddSingleton(settings.Settings);
        services.AddSingleton<ILayoutService>(layouts);
        services.AddSingleton<ISessionContext, EmptySession>();
        services.AddSingleton<IThreadMarshaller>(new ThreadMarshaller(Dispatcher.CurrentDispatcher));
        services.AddSingleton<IThemeService, ThemeManager>();
        services.AddSingleton<IWidgetRegistry, WidgetRegistry>();
        services.AddSingleton<IWidgetWindowService, WidgetWindowService>();
        services.AddSingleton<SidebarWindow>();
        services.AddSingleton<LayoutController>();
        return services.BuildServiceProvider();
    }
    private static void CloseSidebar(ServiceProvider services)
    {
        var sidebar = services.GetRequiredService<SidebarWindow>();
        sidebar.AllowClose();
        sidebar.Close();
    }
    private static void Pump()
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
        timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; };
        timer.Start();
        Dispatcher.PushFrame(frame);
    }
    private sealed class EmptySession : ISessionContext
    {
        public MetricsSnapshot Latest => MetricsSnapshot.Empty;
        public event Action<MetricsSnapshot>? SnapshotReceived { add { } remove { } }
        public bool IsRunning => false;
        public TimeSpan PollInterval => TimeSpan.FromSeconds(1);
    }
}
