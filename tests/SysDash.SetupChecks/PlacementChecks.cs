using System.IO;
using System.Text.Json;
using System.Windows;
using SysDash.Layouts.Models;
using SysDash.Theming.Native;

internal static class PlacementChecks
{
    public static bool Run(string output)
    {
        var checks = new Dictionary<string, bool>();
        var layout = new Layout { Items = new() { new LayoutItem { Placement = new WidgetPlacement
        {
            Docked = false, X = -1200, Y = 300, MonitorDeviceId = "left-monitor",
            MonitorOffsetX = 180, MonitorOffsetY = 240, HeightLimit = 450,
        } } } };
        var restored = JsonSerializer.Deserialize<Layout>(JsonSerializer.Serialize(layout.Clone()))!;
        checks["SnapshotPreservesWidgetIdentity"] = restored.Items[0].InstanceId == layout.Items[0].InstanceId;
        checks["SnapshotPreservesScreenAndCoordinates"] = restored.Items[0].Placement.MonitorDeviceId == "left-monitor" && restored.Items[0].Placement.X == -1200 && restored.Items[0].Placement.MonitorOffsetX == 180 && restored.Items[0].Placement.HeightLimit == 450;
        var sizes = new[] { new Size(320, 550), new Size(320, 430), new Size(320, 390), new Size(320, 500), new Size(320, 420), new Size(320, 620) };
        foreach (Size screen in new[] { new Size(1920, 1080), new Size(1024, 768), new Size(800, 600), new Size(640, 480) })
        {
            Rect work = new(-1600, -100, screen.Width, screen.Height);
            var plan = FloatingPlacement.ArrangeInitial(work, sizes);
            bool valid = plan.Select((p, i) => p.Bounds.Height == sizes[i].Height && p.HeightLimit == 0 &&
                p.Bounds.Left >= work.Left && p.Bounds.Right <= work.Right).All(x => x);
            for (int i = 0; i < plan.Length; i++)
                for (int j = i + 1; j < plan.Length; j++)
                {
                    Rect probe = plan[i].Bounds;
                    probe.Inflate(FloatingPlacement.Gap / 2 - 0.01, FloatingPlacement.Gap / 2 - 0.01);
                    Rect other = plan[j].Bounds;
                    other.Inflate(FloatingPlacement.Gap / 2 - 0.01, FloatingPlacement.Gap / 2 - 0.01);
                    valid &= !probe.IntersectsWith(other);
                }
            checks[$"InitialLayoutPreservesFullHeightWithoutOverlap{screen.Width}x{screen.Height}"] = valid;
        }
        var monitor = new MonitorDescriptor(1, "display2", new Rect(-1280, 0, 1280, 720), new Rect(-1280, 0, 1280, 690), 144, 144, false) { DeviceId = "stable-id" };
        Point pixels = FloatingPlacement.RestorePixels(monitor, 150, 210, 320, 450);
        checks["MixedDpiRestoresMonitorRelativePixels"] = pixels == new Point((-1280 + 150) * 1.5, 210 * 1.5);
        var movedMonitor = monitor with { WorkAreaDip = new Rect(1600, -500, 1280, 690), Number = 7 };
        Point moved = FloatingPlacement.RestorePixels(movedMonitor, 150, 210, 320, 450);
        checks["MonitorReorderPreservesLocalPosition"] = moved == new Point((1600 + 150) * 1.5, (-500 + 210) * 1.5);
        Point edge = FloatingPlacement.RestorePixels(monitor, 1100, 600, 320, 450);
        checks["IntentionalEdgePositionIsNotReset"] = edge == new Point((-1280 + 1100) * 1.5, 600 * 1.5);
        File.WriteAllText(Path.Combine(output, "placement-checks.json"), JsonSerializer.Serialize(checks, new JsonSerializerOptions { WriteIndented = true }));
        return checks.Values.All(x => x);
    }
}
