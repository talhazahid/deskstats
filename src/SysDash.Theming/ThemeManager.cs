using System.Windows;
using SysDash.Core.Diagnostics;

namespace SysDash.Theming;

public sealed class ThemeManager : IThemeService
{
    private const string PackPrefix = "pack://application:,,,/SysDash.Theming;component/Themes/";

    private readonly Dictionary<ThemeKind, string> _uris = new()
    {
        [ThemeKind.Dark] = PackPrefix + "Dark.xaml",
        [ThemeKind.Light] = PackPrefix + "Light.xaml",
    };

    private ResourceDictionary? _applied;
    private ThemeKind _current = ThemeKind.Dark;

    public ThemeKind Current => _current;

    /// <summary>
    /// A fresh copy of a theme dictionary, for windows that use their own theme
    /// (per-widget Dark / Light) instead of the app theme.
    /// </summary>
    public static ResourceDictionary LoadDictionary(ThemeKind kind)
        => new() { Source = new Uri(PackPrefix + (kind == ThemeKind.Light ? "Light.xaml" : "Dark.xaml")) };

    /// <summary>"dark" / "light" → theme; anything else (null, "", "app") → null = follow the app.</summary>
    public static ThemeKind? ParseOverride(string? value)
        => value?.Trim().ToLowerInvariant() switch
        {
            "dark" => ThemeKind.Dark,
            "light" => ThemeKind.Light,
            _ => null,
        };

    public event EventHandler<ThemeKind>? ThemeChanged;

    public void Apply(ResourceDictionary appResources, ThemeKind? kind = null)
    {
        ThemeKind target = kind ?? _current;

        try
        {
            if (_applied is not null)
            {
                appResources.MergedDictionaries.Remove(_applied);
                _applied = null;
            }

            var theme = new ResourceDictionary { Source = new Uri(_uris[target]) };
            appResources.MergedDictionaries.Add(theme);
            _applied = theme;
            _current = target;

            Log.Info($"Theme applied: {target}");
            ThemeChanged?.Invoke(this, target);
        }
        catch (Exception ex)
        {
            Log.Error($"Failed to apply theme {target}", ex);
        }
    }
}