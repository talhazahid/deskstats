using System.Windows;

namespace SysDash.SDK.Widgets;

/// <summary>
/// Runtime state for ONE widget instance. A widget type can be instantiated many
/// times (duplicates). Created by the App's LayoutController.
/// </summary>
public sealed class WidgetInstance
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string WidgetId { get; set; } = string.Empty;

    public WidgetSettings Settings { get; set; } = null!;

    public string? TitleOverride { get; set; }

    /// <summary>Service provider supplied by the composition root.</summary>
    public IServiceProvider Services { get; set; } = null!;

    public IWidget Widget { get; set; } = null!;

    public WidgetViewModel ViewModel { get; set; } = null!;

    public FrameworkElement View { get; set; } = null!;

    /// <summary>Display title (override or widget display name).</summary>
    public string EffectiveTitle => TitleOverride ?? Widget?.DisplayName ?? WidgetId;
}