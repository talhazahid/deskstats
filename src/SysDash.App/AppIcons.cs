using System.Drawing;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace SysDash.App;

/// <summary>
/// The DeskStats logo (Assets\DeskStats.ico — the same file is the exe icon, so
/// Explorer, shortcuts, the Start menu, the tray and every window match).
/// </summary>
public static class AppIcons
{
    private static readonly Uri IconUri = new("pack://application:,,,/DeskStats;component/Assets/DeskStats.ico", UriKind.Absolute);

    /// <summary>Tray icon at the size Windows uses for the notification area on this DPI.</summary>
    public static Icon CreateTrayIcon()
    {
        try
        {
            var info = Application.GetResourceStream(IconUri);
            if (info?.Stream is { } stream)
            {
                using (stream)
                {
                    // Closest frame to the small-icon size on this DPI (16 px at 100 %, 24/32 px on high DPI).
                    int px = GetSystemMetrics(SmCxSmIcon);
                    return new Icon(stream, new System.Drawing.Size(px > 0 ? px : 16, px > 0 ? px : 16));
                }
            }
        }
        catch
        {
            // fall through to the exe's own icon
        }

        return Icon.ExtractAssociatedIcon(Environment.ProcessPath ?? string.Empty) ?? SystemIcons.Application;
    }

    private const int SmCxSmIcon = 49;

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int index);

    /// <summary>Window / title-bar icon (WPF), or null when unavailable.</summary>
    public static ImageSource? WindowIcon()
    {
        try
        {
            return BitmapFrame.Create(IconUri);
        }
        catch
        {
            return null;
        }
    }
}
