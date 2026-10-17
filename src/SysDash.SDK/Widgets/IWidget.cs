using System.Windows;
using SysDash.Core.Models;

namespace SysDash.SDK.Widgets;

/// <summary>
/// Contract every widget type implements: factory that knows how to create
/// settings, build its view model, and render a view for a runtime instance.
/// </summary>
public interface IWidget
{
    /// <summary>Stable identifier persisted in layouts, e.g. "cpu".</summary>
    string Id { get; }

    string DisplayName { get; }

    string Description { get; }

    /// <summary>Grouping for menus: "hardware", "time", "internet", "system".</summary>
    string Category { get; }

    WidgetSettings CreateDefaultSettings();

    WidgetSettings? DeserializeSettings(string json);

    string SerializeSettings(WidgetSettings settings);

    /// <summary>Creates the view for a runtime instance (sets DataContext to the instance view model).</summary>
    FrameworkElement CreateView(WidgetInstance instance);
}

/// <summary>Suggested categories for menu grouping.</summary>
public static class WidgetCategories
{
    public const string Hardware = "hardware";
    public const string Time = "time";
    public const string Internet = "internet";
    public const string System = "system";
}