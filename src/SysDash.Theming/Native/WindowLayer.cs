using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace SysDash.Theming.Native;

/// <summary>Z-order "layer" of a SysDash window (Rainmeter-style).</summary>
public enum WindowLayer
{
    /// <summary>Always above normal windows.</summary>
    Top,

    /// <summary>Behaves like a regular window (comes forward when clicked).</summary>
    Normal,

    /// <summary>Stays behind every other window — sits on the desktop.</summary>
    Bottom,
}

/// <summary>
/// Applies and enforces a <see cref="WindowLayer"/>. "Bottom" is enforced by a
/// WM_WINDOWPOSCHANGING hook that rewrites every z-order change to HWND_BOTTOM,
/// so clicking the window (or Windows reshuffling the z-order) never lifts it
/// above other windows.
/// </summary>
public static class WindowLayerHelper
{
    private const int WmWindowPosChanging = 0x0046;
    private const uint SwpNoZOrder = 0x0004;
    private static readonly IntPtr HwndBottom = new(1);

    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Window, Func<WindowLayer>> Providers = new();

    [StructLayout(LayoutKind.Sequential)]
    private struct WindowPos
    {
        public IntPtr hwnd;
        public IntPtr hwndInsertAfter;
        public int x;
        public int y;
        public int cx;
        public int cy;
        public uint flags;
    }

    public static string ToKey(WindowLayer layer) => layer switch
    {
        WindowLayer.Top => "top",
        WindowLayer.Bottom => "bottom",
        _ => "normal",
    };

    public static WindowLayer Parse(string? key, WindowLayer fallback = WindowLayer.Normal)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return fallback;
        }

        return key.Trim().ToLowerInvariant() switch
        {
            "top" or "topmost" or "alwaysontop" => WindowLayer.Top,
            "bottom" or "desktop" or "back" => WindowLayer.Bottom,
            "normal" => WindowLayer.Normal,
            _ => fallback,
        };
    }

    /// <summary>
    /// Hooks the window so its layer is enforced for its whole lifetime.
    /// <paramref name="layerProvider"/> is queried live, so changing the layer
    /// later only needs a call to <see cref="Apply"/>.
    /// </summary>
    public static void Attach(Window window, Func<WindowLayer> layerProvider)
    {
        Providers.Remove(window);
        Providers.Add(window, layerProvider);

        void Install()
        {
            IntPtr hwnd = new WindowInteropHelper(window).Handle;
            if (hwnd == IntPtr.Zero)
            {
                return;
            }

            HwndSource? source = HwndSource.FromHwnd(hwnd);
            source?.AddHook(WndProc);
            Apply(window, layerProvider());
        }

        if (new WindowInteropHelper(window).Handle != IntPtr.Zero)
        {
            Install();
        }
        else
        {
            window.SourceInitialized += (_, _) => Install();
        }
    }

    /// <summary>Moves the window into the given layer right now.</summary>
    public static void Apply(Window window, WindowLayer layer)
    {
        IntPtr hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero)
        {
            // Not created yet: WPF applies Topmost when the handle is created.
            window.Topmost = layer == WindowLayer.Top;
            return;
        }

        IntPtr insertAfter = layer switch
        {
            WindowLayer.Top => NativeMethods.HwndTopmost,
            WindowLayer.Bottom => HwndBottom,
            _ => NativeMethods.HwndNotTopmost,
        };

        // Keep WPF's own notion in sync without letting it fight the hook.
        if (window.Topmost != (layer == WindowLayer.Top))
        {
            window.Topmost = layer == WindowLayer.Top;
        }

        NativeMethods.SetWindowPos(hwnd, insertAfter, 0, 0, 0, 0,
            NativeMethods.SwpNoMove | NativeMethods.SwpNoSize | NativeMethods.SwpNoActivate);
    }

    private static IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg != WmWindowPosChanging)
        {
            return IntPtr.Zero;
        }

        try
        {
            Window? window = HwndSource.FromHwnd(hwnd)?.RootVisual as Window;
            if (window is null || !Providers.TryGetValue(window, out Func<WindowLayer>? provider))
            {
                return IntPtr.Zero;
            }

            if (provider() != WindowLayer.Bottom)
            {
                return IntPtr.Zero;
            }

            WindowPos pos = Marshal.PtrToStructure<WindowPos>(lParam);
            if ((pos.flags & SwpNoZOrder) == 0)
            {
                pos.hwndInsertAfter = HwndBottom;
                Marshal.StructureToPtr(pos, lParam, false);
            }
        }
        catch
        {
            // Never let a z-order hook break window messages.
        }

        return IntPtr.Zero;
    }
}
