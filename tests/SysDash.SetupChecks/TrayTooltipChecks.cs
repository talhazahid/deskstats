using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using Hardcodet.Wpf.TaskbarNotification;

internal static class TrayTooltipChecks
{
    public static bool Run(string output)
    {
        // No icon or live application state: simulate shell callbacks on an
        // isolated control, including repeated opens and a missing close.
        using var tray = new TaskbarIcon { Visibility = Visibility.Collapsed, ToolTipText = "DeskStats" };
        var checks = new Dictionary<string, bool>();
        checks["PlainTextUsesNoWpfTooltip"] = tray.TrayToolTipResolved is null;
        var callback = typeof(TaskbarIcon).GetMethod("OnToolTipChange", BindingFlags.NonPublic | BindingFlags.Instance)!;
        foreach (bool open in new[] { true, true, false, true, false, true }) callback.Invoke(tray, new object[] { open });
        checks["RepeatedShellCallbacksCannotOpenOrphanPopup"] = tray.TrayToolTipResolved is null;
        tray.ToolTipText = "DeskStats — monitoring";
        callback.Invoke(tray, new object[] { true });
        checks["TextChangeKeepsNativeTooltip"] = tray.ToolTipText == "DeskStats — monitoring" && tray.TrayToolTipResolved is null;
        checks["NoCustomPopupWasConfigured"] = tray.TrayToolTip is null && tray.TrayPopup is null;
        File.WriteAllText(Path.Combine(output, "tray-tooltip-checks.json"), JsonSerializer.Serialize(checks, new JsonSerializerOptions { WriteIndented = true }));
        return checks.Values.All(x => x);
    }
}
