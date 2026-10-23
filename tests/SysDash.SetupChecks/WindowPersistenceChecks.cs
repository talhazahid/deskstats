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
            File.WriteAllText(Path.Combine(output, "initial-window-bounds.json"), JsonSerializer.Serialize(windows.Select(w => new { w.Title, w.Left, w.Top, w.ActualWidth, w.ActualHeight, w.MaxHeight }), new JsonSerializerOptions { WriteIndented = true }));
            checks["CreatesAllSixWidgets"] = windows.Length == 6;
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
            checks["ExitKeepsAllWidgetEntries"] = expected.Count == 6;
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
            checks["RestartRestoresExactMonitorAndPosition"] = items.Count == expected.Count && items.All(i => expected.TryGetValue(i.InstanceId, out var p) &&
                p.Screen == i.Placement.MonitorDeviceId && Math.Abs(p.X - i.Placement.MonitorOffsetX) <= 1 && Math.Abs(p.Y - i.Placement.MonitorOffsetY) <= 1);
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
            checks["RestoreHardwareShowsAllSixWithoutDuplicates"] = controller.Instances.Count() == 6 &&
                LayoutController.DefaultWidgetIds.All(controller.IsWidgetShown) && controller.Instances.Any(i => i.Id == retained);
            foreach (var instance in controller.Instances) controller.SetWidgetLarge(instance.Id, true);
            Pump();
            checks["EveryHardwareWidgetSupportsLargeSize"] = controller.Instances.All(i => controller.IsLargeWidget(i.Id) && Math.Abs(i.View.LayoutTransform.Value.M11 - 1.35) < 0.001);
            checks["LargeWidgetsHaveNoHeightCap"] = Application.Current.Windows.OfType<WidgetWindow>().All(w => double.IsPositiveInfinity(w.MaxHeight) && w.SizeToContent == SizeToContent.Height);
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
            checks["RestartKeepsHiddenRemovedAndLargeState"] = controller.Instances.Count() == 4 &&
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
            controller.Dispose(); CloseSidebar(services);
        }
        File.WriteAllText(Path.Combine(output, "window-persistence-checks.json"), JsonSerializer.Serialize(checks, new JsonSerializerOptions { WriteIndented = true }));
        return checks.Values.All(x => x);
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
