using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

namespace SysDash.Theming.Native;

/// <summary>
/// Keeps widget text readable on ANY background — wallpaper, a folder, an app
/// behind the widget — no matter the theme or card opacity.
///
/// Every ~1.5 s it samples what is actually on screen just around the window
/// (the transparent margin around the card), blends it with the card's own
/// background at its current opacity, and measures the brightness of the result.
/// If the theme's text would be hard to read on that (dark text on a dark
/// wallpaper, light text on a bright window), it switches the window's text
/// colours to the readable set; when the theme's own colours work again it
/// switches back. Hysteresis prevents flicker near the threshold.
/// </summary>
public sealed class AdaptiveContrast : IDisposable
{
    private static readonly (string Key, Color OnDark, Color OnLight)[] TextKeys =
    {
        ("TextPrimaryBrush", Color.FromRgb(0xF5, 0xF5, 0xF7), Color.FromRgb(0x0F, 0x17, 0x2A)),
        ("TextSecondaryBrush", Color.FromRgb(0xD8, 0xD8, 0xDE), Color.FromRgb(0x33, 0x41, 0x55)),
        ("TextTertiaryBrush", Color.FromRgb(0xA8, 0xA8, 0xB2), Color.FromRgb(0x5B, 0x67, 0x7A)),
        ("TrackBrush", Color.FromArgb(0x38, 0xFF, 0xFF, 0xFF), Color.FromArgb(0x26, 0x0F, 0x17, 0x2A)),
    };

    private readonly Window _window;
    private readonly DispatcherTimer _timer;
    private bool? _lightText; // null = theme colours in use

    private AdaptiveContrast(Window window)
    {
        _window = window;
        _timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(1500) };
        _timer.Tick += (_, _) => Refresh();

        window.Loaded += (_, _) =>
        {
            Refresh();
            _timer.Start();
        };
        window.Closed += (_, _) => Dispose();
    }

    /// <summary>Starts automatic contrast for this window.</summary>
    public static AdaptiveContrast Attach(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);
        return new AdaptiveContrast(window);
    }

    /// <summary>Re-evaluates now (call after the theme, opacity or card frame changed).</summary>
    public void Refresh()
    {
        try
        {
            if (!_window.IsLoaded || !_window.IsVisible)
            {
                return;
            }

            // 1) What the text sits on: section tint over the card background.
            Color card = BrushColor(CardAppearance.CardBackgroundKey);
            Color section = BrushColor(CardAppearance.SectionBackgroundKey);
            Color surface = Over(section, card);
            double surfaceAlpha = surface.A / 255.0;

            // 2) What shows through it (only matters when the card is see-through).
            Color behind = surfaceAlpha < 0.97 && SampleAround(out Color sampled) ? sampled : Colors.Gray;
            Color seen = Over(surface, Color.FromArgb(255, behind.R, behind.G, behind.B));
            double luminance = Luminance(seen);

            // 3) The theme's own text: dark text (light theme) or light text (dark theme)?
            bool themeLightText = Luminance(ThemeTextColor()) > 0.5;

            bool currentLight = _lightText ?? themeLightText;
            bool wantLight = currentLight ? luminance < 0.55 : luminance < 0.40;

            if (wantLight == themeLightText)
            {
                Clear();
            }
            else
            {
                Use(wantLight);
            }
        }
        catch
        {
            // Sampling is best-effort; keep the current colours.
        }
    }

    public void Dispose() => _timer.Stop();

    // ---- colours -------------------------------------------------------------

    private void Use(bool lightText)
    {
        if (_lightText == lightText)
        {
            return;
        }

        foreach ((string key, Color onDark, Color onLight) in TextKeys)
        {
            var brush = new SolidColorBrush(lightText ? onDark : onLight);
            brush.Freeze();
            _window.Resources[key] = brush;
        }

        _lightText = lightText;
    }

    private void Clear()
    {
        if (_lightText is null)
        {
            return;
        }

        foreach ((string key, _, _) in TextKeys)
        {
            _window.Resources.Remove(key);
        }

        _lightText = null;
    }

    private Color ThemeTextColor()
    {
        // Look past our own override: theme dictionaries (window-merged or app).
        foreach (ResourceDictionary merged in _window.Resources.MergedDictionaries)
        {
            if (merged["TextPrimaryColor"] is Color own)
            {
                return own;
            }
        }

        return Application.Current?.TryFindResource("TextPrimaryColor") is Color app ? app : Colors.White;
    }

    private Color BrushColor(string key)
        => _window.TryFindResource(key) is SolidColorBrush b ? b.Color : Colors.Transparent;

    private static Color Over(Color top, Color bottom)
    {
        double ta = top.A / 255.0;
        double ba = bottom.A / 255.0;
        double outA = ta + ba * (1 - ta);
        if (outA <= 0)
        {
            return Colors.Transparent;
        }

        byte Mix(byte t, byte b) => (byte)Math.Round((t * ta + b * ba * (1 - ta)) / outA);
        return Color.FromArgb((byte)Math.Round(outA * 255), Mix(top.R, bottom.R), Mix(top.G, bottom.G), Mix(top.B, bottom.B));
    }

    /// <summary>WCAG relative luminance 0 (black) – 1 (white).</summary>
    private static double Luminance(Color c)
    {
        static double Lin(byte v)
        {
            double s = v / 255.0;
            return s <= 0.04045 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4);
        }

        return 0.2126 * Lin(c.R) + 0.7152 * Lin(c.G) + 0.0722 * Lin(c.B);
    }

    // ---- screen sampling -----------------------------------------------------

    /// <summary>Average colour of thin strips along the window's outer edge (screen pixels).</summary>
    private bool SampleAround(out Color average)
    {
        average = Colors.Gray;
        IntPtr hwnd = new WindowInteropHelper(_window).Handle;
        if (hwnd == IntPtr.Zero || !GetWindowRect(hwnd, out Rect32 r))
        {
            return false;
        }

        int w = r.Right - r.Left;
        int h = r.Bottom - r.Top;
        if (w < 8 || h < 8)
        {
            return false;
        }

        long sr = 0, sg = 0, sb = 0, n = 0;
        void Add(int x, int y, int cw, int ch)
        {
            if (Capture(x, y, cw, ch, out long rr, out long gg, out long bb, out long count))
            {
                sr += rr;
                sg += gg;
                sb += bb;
                n += count;
            }
        }

        const int t = 3; // strip thickness, inside the transparent margin
        Add(r.Left, r.Top, w, t);
        Add(r.Left, r.Bottom - t, w, t);
        Add(r.Left, r.Top + t, t, h - 2 * t);
        Add(r.Right - t, r.Top + t, t, h - 2 * t);

        if (n == 0)
        {
            return false;
        }

        average = Color.FromRgb((byte)(sr / n), (byte)(sg / n), (byte)(sb / n));
        return true;
    }

    private static bool Capture(int x, int y, int w, int h, out long r, out long g, out long b, out long count)
    {
        r = g = b = count = 0;
        if (w <= 0 || h <= 0)
        {
            return false;
        }

        IntPtr screen = GetDC(IntPtr.Zero);
        if (screen == IntPtr.Zero)
        {
            return false;
        }

        IntPtr mem = CreateCompatibleDC(screen);
        IntPtr bmp = CreateCompatibleBitmap(screen, w, h);
        IntPtr old = SelectObject(mem, bmp);
        try
        {
            if (!BitBlt(mem, 0, 0, w, h, screen, x, y, SrcCopy))
            {
                return false;
            }

            var info = new BitmapInfoHeader
            {
                Size = (uint)Marshal.SizeOf<BitmapInfoHeader>(),
                Width = w,
                Height = -h, // top-down
                Planes = 1,
                BitCount = 32,
            };

            var pixels = new int[w * h];
            SelectObject(mem, old); // a bitmap must not be selected while GetDIBits reads it
            old = IntPtr.Zero;
            if (GetDIBits(mem, bmp, 0, (uint)h, pixels, ref info, 0) == 0)
            {
                return false;
            }

            // Sample every 2nd pixel: plenty for an average, half the work.
            for (int i = 0; i < pixels.Length; i += 2)
            {
                int p = pixels[i];
                b += p & 0xFF;
                g += (p >> 8) & 0xFF;
                r += (p >> 16) & 0xFF;
                count++;
            }

            return count > 0;
        }
        finally
        {
            if (old != IntPtr.Zero)
            {
                SelectObject(mem, old);
            }

            DeleteObject(bmp);
            DeleteDC(mem);
            ReleaseDC(IntPtr.Zero, screen);
        }
    }

    private const uint SrcCopy = 0x00CC0020;

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect32
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BitmapInfoHeader
    {
        public uint Size;
        public int Width;
        public int Height;
        public ushort Planes;
        public ushort BitCount;
        public uint Compression;
        public uint SizeImage;
        public int XPelsPerMeter;
        public int YPelsPerMeter;
        public uint ClrUsed;
        public uint ClrImportant;
    }

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hWnd, out Rect32 rect);

    [DllImport("user32.dll")]
    private static extern IntPtr GetDC(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(IntPtr hWnd, IntPtr hdc);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateCompatibleDC(IntPtr hdc);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateCompatibleBitmap(IntPtr hdc, int width, int height);

    [DllImport("gdi32.dll")]
    private static extern IntPtr SelectObject(IntPtr hdc, IntPtr obj);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr obj);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteDC(IntPtr hdc);

    [DllImport("gdi32.dll")]
    private static extern bool BitBlt(IntPtr dest, int x, int y, int w, int h, IntPtr src, int sx, int sy, uint rop);

    [DllImport("gdi32.dll")]
    private static extern int GetDIBits(IntPtr hdc, IntPtr bitmap, uint start, uint lines, [Out] int[] bits, ref BitmapInfoHeader info, uint usage);
}
