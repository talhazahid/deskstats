using System.Windows;

namespace SysDash.Theming;

/// <summary>
/// Swaps the active theme ResourceDictionary in Application.Resources.
/// Widgets and windows reference theme brushes through DynamicResource so a
/// switch is applied live to every open window.
/// </summary>
public interface IThemeService
{
    ThemeKind Current { get; }

    event EventHandler<ThemeKind>? ThemeChanged;

    /// <summary>Applies the given theme (or re-applies the current one, e.g. after tint changes).</summary>
    void Apply(ResourceDictionary appResources, ThemeKind? kind = null);
}