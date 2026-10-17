using System.Windows;
using System.Windows.Media;

namespace SysDash.Theming;

/// <summary>
/// Per-window card look: background opacity and the outer card frame.
///
/// Widget cards (<c>WidgetCard</c> / <c>WidgetSection</c> styles) paint with the
/// <c>Card*Brush</c> resources. The theme dictionaries define normal defaults;
/// this helper overrides them inside one window's own Resources, so:
///   • opacity fades only the card BACKGROUNDS — text, rings and graphs stay
///     fully solid (unlike Window.Opacity, which faded everything);
///   • "no frame" removes the outer card (its background, border and stroke) and
///     gives each inner section the solid card colour instead, so sections float
///     on their own.
/// Call again after a theme switch (colours come from the active theme).
/// </summary>
public static class CardAppearance
{
    public const string CardBackgroundKey = "CardBackgroundBrush";
    public const string CardBorderKey = "CardBorderBrush";
    public const string SectionBackgroundKey = "CardSectionBrush";
    public const string SectionBorderKey = "CardSectionBorderBrush";

    /// <param name="scope">Window (or element) whose cards are affected.</param>
    /// <param name="opacity">Background opacity 0–1 (text is never faded).</param>
    /// <param name="showFrame">False removes the outer card frame.</param>
    /// <param name="keepHitTest">
    /// Keep backgrounds at least 1/255 alpha so a transparent (layered) window can
    /// still be dragged / right-clicked on empty card areas.
    /// </param>
    public static void Apply(FrameworkElement scope, double opacity, bool showFrame, bool keepHitTest = true)
    {
        ArgumentNullException.ThrowIfNull(scope);
        opacity = Math.Clamp(double.IsNaN(opacity) ? 1 : opacity, 0, 1);

        Color panel = ColorOf(scope, "PanelBackgroundColor", Color.FromArgb(0xF2, 0x1C, 0x1C, 0x20));
        Color border = ColorOf(scope, "PanelBorderColor", Color.FromArgb(0x2E, 0xFF, 0xFF, 0xFF));
        Color section = ColorOf(scope, "SectionBackgroundColor", Color.FromArgb(0x0F, 0xFF, 0xFF, 0xFF));
        Color sectionBorder = ColorOf(scope, "SectionBorderColor", Color.FromArgb(0x0A, 0xFF, 0xFF, 0xFF));

        byte minAlpha = keepHitTest ? (byte)1 : (byte)0;
        Color cardBg, cardBorder, sectionBg, sectionStroke;

        if (showFrame)
        {
            cardBg = Fade(panel, opacity, minAlpha);
            cardBorder = Fade(border, opacity, 0);
            sectionBg = Fade(section, opacity, 0);
            sectionStroke = Fade(sectionBorder, opacity, 0);
        }
        else
        {
            // No outer card: sections get the card colour (section tint composited
            // over the panel), so they read like small standalone cards.
            cardBg = keepHitTest ? Color.FromArgb(1, 0, 0, 0) : Colors.Transparent;
            cardBorder = Colors.Transparent;
            sectionBg = Fade(Over(section, panel), opacity, minAlpha);
            sectionStroke = Fade(border, opacity * 0.7, 0);
        }

        Set(scope, CardBackgroundKey, cardBg);
        Set(scope, CardBorderKey, cardBorder);
        Set(scope, SectionBackgroundKey, sectionBg);
        Set(scope, SectionBorderKey, sectionStroke);
    }

    private static void Set(FrameworkElement scope, string key, Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        scope.Resources[key] = brush;
    }

    private static Color ColorOf(FrameworkElement scope, string key, Color fallback)
    {
        // The window's own theme (per-widget Dark/Light) wins over the app theme.
        object? value = scope.TryFindResource(key) ?? Application.Current?.TryFindResource(key);
        return value is Color c ? c : fallback;
    }

    private static Color Fade(Color c, double factor, byte minAlpha)
    {
        byte a = (byte)Math.Round(c.A * Math.Clamp(factor, 0, 1));
        return Color.FromArgb(Math.Max(a, minAlpha), c.R, c.G, c.B);
    }

    /// <summary>Source-over composite of <paramref name="top"/> on <paramref name="bottom"/>.</summary>
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
}
