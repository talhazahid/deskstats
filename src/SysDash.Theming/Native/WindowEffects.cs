using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using SysDash.Core.Diagnostics;

namespace SysDash.Theming.Native;

/// <summary>Win11 DWM backdrop styles (DWMWA_SYSTEMBACKDROP_TYPE).</summary>
public enum SystemBackdropStyle
{
    None = 0,
    Acrylic = 3,
    Mica = 2,
    Tabbed = 4,
}

/// <summary>Window corner preference (DWMWA_WINDOW_CORNER_PREFERENCE).</summary>
public enum WindowCornerPreference
{
    Default = 0,
    DoNotRound = 1,
    Round = 2,
    RoundSmall = 3,
}

/// <summary>
/// High-level window chrome helpers used by the App shell and widget windows:
/// DWM system backdrops (Acrylic/Mica), rounded corners, dark title bar,
/// click-through, tool-window (no Alt+Tab), and always-on-top.
///
/// Every method degrades gracefully: on an OS without the feature it simply
/// does nothing instead of throwing.
/// </summary>
public static class WindowEffects
{
    public static IntPtr GetHandle(Window window)
        => new WindowInteropHelper(window).EnsureHandle();

    /// <summary>Applies a Win11 DWM system backdrop. Returns true when the OS honored it.</summary>
    public static bool ApplySystemBackdrop(Window window, SystemBackdropStyle style)
    {
        if (style == SystemBackdropStyle.None)
        {
            return false;
        }

        try
        {
            IntPtr hwnd = GetHandle(window);
            int value = (int)style;
            return NativeMethods.DwmSetWindowAttribute(
                       hwnd, NativeMethods.DwmwaSystemBackdropType, ref value, sizeof(int)) == 0;
        }
        catch (Exception ex)
        {
            Log.Debug($"System backdrop unavailable: {ex.Message}");
            return false;
        }
    }

    /// <summary>Win10 acrylic fallback via SetWindowCompositionAttribute (ACCENT_ENABLE_ACRYLICBLURBEHIND).</summary>
    public static bool ApplyAcrylicFallback(Window window, Color tint, double tintOpacity)
    {
        try
        {
            IntPtr hwnd = GetHandle(window);
            int alpha = (int)Math.Round(Math.Clamp(tintOpacity, 0, 1) * 255);
            int gradient =
                (alpha << 24) |
                (tint.R << 16) |
                (tint.G << 8) |
                tint.B;

            var accent = new NativeMethods.AccentPolicy
            {
                AccentState = NativeMethods.AccentEnableAcrylicBlurBehind,
                AccentFlags = 2, // draw all borders of the window
                GradientColor = gradient,
            };

            var data = new NativeMethods.WindowCompositionAttributeData
            {
                Attribute = NativeMethods.WindowCompositionAttributeAccentPolicy,
                Data = Marshal.AllocHGlobal(Marshal.SizeOf<NativeMethods.AccentPolicy>()),
                SizeOfData = Marshal.SizeOf<NativeMethods.AccentPolicy>(),
            };

            try
            {
                Marshal.StructureToPtr(accent, data.Data, false);
                return NativeMethods.SetWindowCompositionAttribute(hwnd, ref data) == 0;
            }
            finally
            {
                Marshal.FreeHGlobal(data.Data);
            }
        }
        catch (Exception ex)
        {
            Log.Debug($"Acrylic fallback unavailable: {ex.Message}");
            return false;
        }
    }

    /// <summary>Sets Win11 rounded-corner preference (no-op on Windows 10).</summary>
    public static void ApplyCornerPreference(Window window, WindowCornerPreference preference)
    {
        if (preference == WindowCornerPreference.Default ||
            !OperatingSystem.IsWindowsVersionAtLeast(11))
        {
            return;
        }

        try
        {
            IntPtr hwnd = GetHandle(window);
            int value = (int)preference;
            NativeMethods.DwmSetWindowAttribute(
                hwnd, NativeMethods.DwmwaWindowCornerPreference, ref value, sizeof(int));
        }
        catch (Exception ex)
        {
            Log.Debug($"Corner preference unavailable: {ex.Message}");
        }
    }

    /// <summary>Dark immersive title bar (visible for tool windows on some builds).</summary>
    public static void ApplyImmersiveDarkMode(Window window, bool dark)
    {
        try
        {
            IntPtr hwnd = GetHandle(window);
            int value = dark ? 1 : 0;
            NativeMethods.DwmSetWindowAttribute(
                hwnd, NativeMethods.DwmwaUseImmersiveDarkMode, ref value, sizeof(int));
        }
        catch (Exception ex)
        {
            Log.Debug($"Dark title bar unavailable: {ex.Message}");
        }
    }

    /// <summary>Toggles click-through (WS_EX_TRANSPARENT + no activation).</summary>
    public static void SetClickThrough(Window window, bool clickThrough)
    {
        IntPtr hwnd = GetHandle(window);
        int style = NativeMethods.GetWindowLong(hwnd, NativeMethods.GwlExStyle);
        int updated = style;

        if (clickThrough)
        {
            updated |= NativeMethods.WsExTransparent | NativeMethods.WsExNoActivate;
        }
        else
        {
            updated &= ~NativeMethods.WsExTransparent;
            // Keep NoActivate cleared only when we put it there.
            if ((style & NativeMethods.WsExNoActivate) != 0)
            {
                updated &= ~NativeMethods.WsExNoActivate;
            }
        }

        if (updated != style)
        {
            NativeMethods.SetWindowLongEx(hwnd, NativeMethods.GwlExStyle, new IntPtr(updated));
            NativeMethods.SetWindowPos(hwnd, IntPtr.Zero, 0, 0, 0, 0,
                NativeMethods.SwpNoMove | NativeMethods.SwpNoSize | NativeMethods.SwpNoActivate | NativeMethods.SwpFrameChanged);
        }
    }

    /// <summary>True when the window currently has WS_EX_TRANSPARENT set (click-through active).</summary>
    public static bool IsClickThrough(Window window)
    {
        IntPtr hwnd = GetHandle(window);
        int style = NativeMethods.GetWindowLong(hwnd, NativeMethods.GwlExStyle);
        return (style & NativeMethods.WsExTransparent) != 0;
    }

    /// <summary>Removes the window from Alt+Tab and the taskbar.</summary>
    public static void SetToolWindow(Window window, bool toolWindow)
    {
        IntPtr hwnd = GetHandle(window);
        int style = NativeMethods.GetWindowLong(hwnd, NativeMethods.GwlExStyle);
        int updated = toolWindow ? style | NativeMethods.WsExToolWindow : style & ~NativeMethods.WsExToolWindow;
        if (updated != style)
        {
            NativeMethods.SetWindowLongEx(hwnd, NativeMethods.GwlExStyle, new IntPtr(updated));
            NativeMethods.SetWindowPos(hwnd, IntPtr.Zero, 0, 0, 0, 0,
                NativeMethods.SwpNoMove | NativeMethods.SwpNoSize | NativeMethods.SwpNoActivate | NativeMethods.SwpFrameChanged);
        }
    }

    public static void SetAlwaysOnTop(Window window, bool alwaysOnTop)
    {
        IntPtr hwnd = GetHandle(window);
        NativeMethods.SetWindowPos(hwnd,
            alwaysOnTop ? NativeMethods.HwndTopmost : NativeMethods.HwndNotTopmost,
            0, 0, 0, 0,
            NativeMethods.SwpNoMove | NativeMethods.SwpNoSize | NativeMethods.SwpNoActivate);
        window.Topmost = alwaysOnTop;
    }

    /// <summary>Cursor position in DIPs (uses the dpi of the monitor the cursor is on).</summary>
    public static Point GetCursorPositionDip()
    {
        if (!NativeMethods.GetCursorPos(out NativeMethods.POINT pt))
        {
            return new Point(0, 0);
        }

        IntPtr monitor = NativeMethods.MonitorFromPoint(pt, NativeMethods.MonitorDefaultToNearest);
        NativeMethods.GetDpiForMonitor(monitor, 0, out int dpiX, out int dpiY);
        double scaleX = dpiX > 0 ? 96.0 / dpiX : 1.0;
        double scaleY = dpiY > 0 ? 96.0 / dpiY : 1.0;
        return new Point(pt.X * scaleX, pt.Y * scaleY);
    }
}