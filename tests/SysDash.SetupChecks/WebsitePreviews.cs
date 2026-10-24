using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using SysDash.App;
using SysDash.Core.Models;
using SysDash.SDK.Session;
using SysDash.SDK.Widgets;

internal static class WebsitePreviews
{
    public static void Render(string output)
    {
        var registry = new WidgetRegistry();
        foreach (string id in new[] { "cpu", "gpu", "ram", "sensors", "network", "drive", "power" })
            RenderWidget(registry, output, id, 340, "dark");
        RenderWidget(registry, output, "gpu", 720, "dark", "resize");
        RenderWidget(registry, output, "cpu", 340, "light", "theme-light");
    }

    private static void RenderWidget(WidgetRegistry registry, string output, string id, int width, string theme, string? name = null)
    {
        Application.Current.Resources.MergedDictionaries[1].Source = new Uri($"pack://application:,,,/SysDash.Theming;component/Themes/{(theme == "light" ? "Light" : "Dark")}.xaml");
        var widget = registry.Find(id)!;
        var settings = widget.CreateDefaultSettings();
        if (settings is SysDash.Widgets.Network.NetworkSettings network) network.ShowPublicIp = false;
        var instance = new WidgetInstance { WidgetId = id, Widget = widget, Services = new EmptyServices(), Settings = settings };
        widget.CreateView(instance);
        var session = new SampleSession();
        instance.ViewModel.Activate(session);
        for (int i = 0; i < 90; i++) session.Emit(i);
        // Allow async process sampling and UI-bound collections to settle.
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(800) };
        timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; };
        timer.Start(); Dispatcher.PushFrame(frame);
        var view = instance.View;
        view.Measure(new Size(width, double.PositiveInfinity));
        view.Arrange(new Rect(0, 0, width, view.DesiredSize.Height));
        view.UpdateLayout();
        Application.Current.Dispatcher.Invoke(() => { }, DispatcherPriority.Render);
        var bitmap = new RenderTargetBitmap(width, (int)Math.Ceiling(view.ActualHeight), 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(view);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using (var file = File.Create(Path.Combine(output, "widget-" + (name ?? id) + ".png"))) encoder.Save(file);
        instance.ViewModel.Dispose();
    }

    private sealed class EmptyServices : IServiceProvider
    {
        public object? GetService(Type type) => type == typeof(SysDash.Core.Threading.IThreadMarshaller)
            ? new ThreadMarshaller(Dispatcher.CurrentDispatcher) : null;
    }
    private sealed class SampleSession : ISessionContext
    {
        public MetricsSnapshot Latest { get; private set; } = MetricsSnapshot.Empty;
        public event Action<MetricsSnapshot>? SnapshotReceived;
        public bool IsRunning => true;
        public TimeSpan PollInterval => TimeSpan.FromSeconds(1);
        public void Emit(int tick)
        {
            double load = 32 + 18 * Math.Sin(tick * .43);
            Latest = new MetricsSnapshot(DateTimeOffset.Now.AddSeconds(tick),
                new CpuReading("AMD Ryzen 5 5600X", load, 3700, 64, 1.05, 1240, new double[] { 24, 36, 15, 47, 30, 18, 52, 21, 39, 28, 12, 41 }, null, 45),
                new RamReading(18.4 * 1073741824, 32 * 1073741824d, 57.5, 13.6 * 1073741824, 24.2 * 1073741824, 48 * 1073741824d),
                new[] { new GpuReading("Radeon RX 6600", load, 42, 1320, 1740, 58, .8, 24, 3.36 * 1073741824, 8 * 1073741824d) },
                new[] { new DriveReading("C:", 512e9, 285e9, 227e9, (6 + load / 10) * 1e6, 2.4e6), new DriveReading("D:", 2e12, .7e12, 1.3e12, 1.2e6, .4e6) },
                new[] { new NetworkAdapterReading("Ethernet", (3 + load / 20) * 1e6, 420000, "192.168.1.24", true) },
                new[] { new SensorReading("CPU", "Package", "power", 45), new SensorReading("GPU · Radeon RX 6600", "ASIC / chip power", "power", 82), new SensorReading("CPU", "Core", "temperature", 64), new SensorReading("GPU", "Core", "temperature", 58), new SensorReading("Board", "System", "temperature", 42), new SensorReading("Board", "VRM", "temperature", 48), new SensorReading("CPU", "CPU Fan", "fan", 1240), new SensorReading("GPU", "GPU Fan", "fan", 820) });
            SnapshotReceived?.Invoke(Latest);
        }
    }
}
