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
        checks["TaskbarBoundaryClampsManualAndSavedPositions"] = edge == new Point((-1280 + 944) * 1.5, 224 * 1.5);
        foreach (Size screen in new[] { new Size(1920, 1080), new Size(1024, 768), new Size(640, 480) })
        {
            Rect work = new(0, 0, screen.Width, screen.Height);
            var fit = FloatingPlacement.FitWithin(work, sizes, sizes.Select(_ => new Point(0, 0)).ToArray());
            Rect safe = work; safe.Inflate(-FloatingPlacement.Margin, -FloatingPlacement.Margin);
            bool valid = fit.Bounds.All(safe.Contains);
            for (int i = 0; i < fit.Bounds.Length; i++)
                for (int j = i + 1; j < fit.Bounds.Length; j++)
                {
                    Rect padded = fit.Bounds[i]; padded.Inflate(-FloatingPlacement.WidgetInset * fit.Scale, -FloatingPlacement.WidgetInset * fit.Scale);
                    padded.Inflate(FloatingPlacement.Gap - 0.1, FloatingPlacement.Gap - 0.1);
                    Rect other = fit.Bounds[j]; other.Inflate(-FloatingPlacement.WidgetInset * fit.Scale, -FloatingPlacement.WidgetInset * fit.Scale);
                    valid &= !padded.IntersectsWith(other);
                }
            checks[$"BalancedInsideTaskbarBoundary{screen.Width}x{screen.Height}"] = valid;
        }
        var compactSizes = new[] { new Size(280, 500), new Size(280, 400), new Size(280, 450), new Size(280, 350) };
        var compact = FloatingPlacement.FitWithin(new Rect(0, 0, 3840, 1080), compactSizes,
            new[] { new Point(0, 0), new Point(1200, 500), new Point(2400, 0), new Point(3500, 600) });
        var columnXs = compact.Bounds.Select(r => r.Left).Distinct().OrderBy(x => x).ToArray();
        checks["DefaultGapIsTwenty"] = FloatingPlacement.Gap == 20;
        var fixedCard = new Rect(810, 110, 300, 380);
        var dropped = FloatingPlacement.FitDropped(new Rect(0, 0, 1920, 1080), new Size(320, 400),
            new Point(780, 100), new[] { fixedCard }, new Thickness(10));
        checks["OnlyDraggedWidgetAdjustsBesideFixedCard"] = fixedCard == new Rect(810, 110, 300, 380) && dropped.Bounds.Right <= fixedCard.Left;
        var leftDrop = FloatingPlacement.FitWithin(new Rect(0, 0, 1920, 1080),
            new[] { new Size(320, 400) }, new[] { new Point(32, 64) }, manual: new[] { true });
        checks["ManualLeftDropDoesNotJumpToRightEdge"] = leftDrop.Bounds[0].TopLeft == new Point(32, 64);
        var middleDrop = FloatingPlacement.FitWithin(new Rect(0, 0, 1920, 1080),
            new[] { new Size(320, 400), new Size(320, 300) }, new[] { new Point(600, 200), new Point(1500, 30) }, manual: new[] { true, false });
        checks["ManualMiddleDropAndOtherWidgetsStayPut"] = middleDrop.Bounds[0].TopLeft == new Point(600, 200) && middleDrop.Bounds[1].TopLeft == new Point(1500, 30);
        var primary = new MonitorDescriptor(0, "main", new Rect(0, 0, 1920, 1080), new Rect(0, 0, 1920, 1040), 96, 96, true);
        var highDpi = new MonitorDescriptor(1, "right", new Rect(1280, 0, 1600, 900), new Rect(1280, 0, 1600, 860), 144, 144, false);
        var leftScreen = new MonitorDescriptor(2, "left", new Rect(-1280, 0, 1280, 720), new Rect(-1280, 0, 1280, 690), 96, 96, false);
        var topScreen = new MonitorDescriptor(3, "above", new Rect(0, -1080, 1920, 1080), new Rect(0, -1080, 1920, 1040), 96, 96, false);
        var screens = new[] { primary, highDpi, leftScreen, topScreen };
        checks["DragCrossesRightEdgeUsingPointerNotWidgetCenter"] = FloatingPlacement.MonitorForPointer(screens, new Point(1921, 200)) == highDpi;
        checks["DragReturnsToMainScreen"] = FloatingPlacement.MonitorForPointer(screens, new Point(1919, 200)) == primary;
        checks["DragSupportsNegativeMonitorCoordinates"] = FloatingPlacement.MonitorForPointer(screens, new Point(-1, 200)) == leftScreen;
        checks["DragSupportsVerticallyStackedMonitors"] = FloatingPlacement.MonitorForPointer(screens, new Point(200, -1)) == topScreen;
        var custom = FloatingPlacement.FitWithin(new Rect(0, 0, 3840, 1080), compactSizes,
            compactSizes.Select(_ => new Point()).ToArray(), new Thickness(15, 5, 35, 25));
        var customXs = custom.Bounds.Select(r => r.Left).Distinct().OrderBy(x => x).ToArray();
        checks["CustomLeftRightSpacingApplied"] = customXs.Length == 2 && customXs[1] - customXs[0] - 280 + 20 == 50;
        checks["CustomTopBottomSpacingApplied"] = custom.Bounds.GroupBy(r => r.Left).All(c =>
        {
            var rows = c.OrderBy(r => r.Top).ToArray();
            return rows.Zip(rows.Skip(1), (a, b) => b.Top - a.Bottom + 20 == 30).All(x => x);
        });
        checks["ScatteredSavedPositionsBecomeTwoAdjacentColumns"] = compact.Scale == 1 && columnXs.Length == 2 && columnXs[1] - columnXs[0] == 280;
        checks["CompactColumnsUseTwentyPixelVerticalGaps"] = compact.Bounds.GroupBy(r => r.Left).All(column =>
        {
            var rows = column.OrderBy(r => r.Top).ToArray();
            return rows.Zip(rows.Skip(1), (a, b) => b.Top - a.Bottom + 2 * FloatingPlacement.WidgetInset == 20).All(x => x);
        });
        File.WriteAllText(Path.Combine(output, "placement-checks.json"), JsonSerializer.Serialize(checks, new JsonSerializerOptions { WriteIndented = true }));
        return checks.Values.All(x => x);
    }
}
