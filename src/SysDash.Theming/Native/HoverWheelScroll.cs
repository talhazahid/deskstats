using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;

namespace SysDash.Theming.Native;

/// <summary>
/// Scroll-on-hover for windows that are never focused (the sidebar).
///
/// Windows only sends WM_MOUSEWHEEL to the *focused* window, unless
/// "Scroll inactive windows when I hover over them" kicks in — and that feature
/// does not reach elevated (administrator) windows like DeskStats. So hovering
/// the sidebar and spinning the wheel did nothing until it was clicked.
///
/// Fix: while the cursor is over the window (MouseEnter → MouseLeave) a
/// low-level mouse hook watches for wheel input. When the window under the
/// cursor is ours, the nearest scrollable ScrollViewer under the cursor is
/// scrolled and the event is consumed (so nothing scrolls twice). The hook is
/// removed as soon as the cursor leaves, so it costs nothing the rest of the time.
/// </summary>
public sealed class HoverWheelScroll : IDisposable
{
    private const int WhMouseLl = 14;
    private const int WmMouseWheel = 0x020A;
    private const uint GaRoot = 2;

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MsllHookStruct
    {
        public NativePoint Pt;
        public uint MouseData;
        public uint Flags;
        public uint Time;
        public IntPtr ExtraInfo;
    }

    private delegate IntPtr LowLevelMouseProc(int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelMouseProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern IntPtr WindowFromPoint(NativePoint point);

    [DllImport("user32.dll")]
    private static extern IntPtr GetAncestor(IntPtr hwnd, uint flags);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandle(string? moduleName);

    private readonly Window _window;
    private readonly ScrollViewer _fallback;
    private readonly LowLevelMouseProc _proc; // kept alive for the native hook
    private IntPtr _hook;
    private IntPtr _hwnd;

    private HoverWheelScroll(Window window, ScrollViewer fallback)
    {
        _window = window;
        _fallback = fallback;
        _proc = HookProc;

        window.MouseEnter += (_, _) => Install();
        window.MouseLeave += (_, _) => Uninstall();
        window.Closed += (_, _) => Dispose();
    }

    /// <summary>Enables scroll-on-hover for <paramref name="window"/>; <paramref name="fallback"/> scrolls when nothing more specific is under the cursor.</summary>
    public static HoverWheelScroll Attach(Window window, ScrollViewer fallback)
    {
        ArgumentNullException.ThrowIfNull(window);
        ArgumentNullException.ThrowIfNull(fallback);
        return new HoverWheelScroll(window, fallback);
    }

    private void Install()
    {
        if (_hook != IntPtr.Zero)
        {
            return;
        }

        _hwnd = new WindowInteropHelper(_window).Handle;
        if (_hwnd == IntPtr.Zero)
        {
            return;
        }

        _hook = SetWindowsHookEx(WhMouseLl, _proc, GetModuleHandle(null), 0);
    }

    private void Uninstall()
    {
        if (_hook == IntPtr.Zero)
        {
            return;
        }

        UnhookWindowsHookEx(_hook);
        _hook = IntPtr.Zero;
    }

    private IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && wParam == (IntPtr)WmMouseWheel)
        {
            try
            {
                var info = Marshal.PtrToStructure<MsllHookStruct>(lParam);
                IntPtr under = WindowFromPoint(info.Pt);
                IntPtr root = under == IntPtr.Zero ? IntPtr.Zero : GetAncestor(under, GaRoot);

                if (root == _hwnd)
                {
                    int delta = unchecked((short)(info.MouseData >> 16));
                    if (ScrollAt(info.Pt, delta))
                    {
                        return (IntPtr)1; // handled — don't let it scroll anything else
                    }
                }
                else
                {
                    // Cursor left without a MouseLeave (fast exit) — drop the hook.
                    _window.Dispatcher.BeginInvoke(new Action(Uninstall));
                }
            }
            catch
            {
                // never break system mouse input
            }
        }

        return CallNextHookEx(_hook, nCode, wParam, lParam);
    }

    private bool ScrollAt(NativePoint screenPx, int delta)
    {
        if (delta == 0)
        {
            return false;
        }

        Point local = _window.PointFromScreen(new Point(screenPx.X, screenPx.Y));
        ScrollViewer? target = FindScrollable(local) ?? (_fallback.ScrollableHeight > 0 ? _fallback : null);
        if (target is null)
        {
            return false;
        }

        int lines = SystemParameters.WheelScrollLines > 0 ? SystemParameters.WheelScrollLines : 3;
        double step = delta / 120.0 * lines * 16;
        target.ScrollToVerticalOffset(Math.Clamp(target.VerticalOffset - step, 0, target.ScrollableHeight));
        return true;
    }

    private ScrollViewer? FindScrollable(Point local)
    {
        HitTestResult? hit = VisualTreeHelper.HitTest(_window, local);
        DependencyObject? node = hit?.VisualHit;
        while (node is not null)
        {
            if (node is ScrollViewer sv && sv.ScrollableHeight > 0)
            {
                return sv;
            }

            node = node is Visual or System.Windows.Media.Media3D.Visual3D
                ? VisualTreeHelper.GetParent(node)
                : LogicalTreeHelper.GetParent(node);
        }

        return null;
    }

    public void Dispose() => Uninstall();
}
