using System.Reflection;
using SysDash.Core.Diagnostics;
using SysDash.SDK.Widgets;

namespace SysDash.App;

/// <summary>
/// Discovers widget implementations via the WidgetAttribute and exposes them to
/// menus, the layout controller and the settings dialogs.
/// </summary>
public sealed class WidgetRegistry : IWidgetRegistry
{
    private readonly Dictionary<string, IWidget> _widgets = new(StringComparer.OrdinalIgnoreCase);

    public WidgetRegistry()
    {
        // The widgets assembly is referenced by App, so we locate it via one of its types.
        Assembly assembly = typeof(SysDash.Widgets.Clock.ClockWidget).Assembly;

        foreach (Type type in assembly.GetTypes())
        {
            if (type.IsAbstract || type.IsInterface || !typeof(IWidget).IsAssignableFrom(type))
            {
                continue;
            }

            try
            {
                var widget = (IWidget)Activator.CreateInstance(type)!;
                if (string.IsNullOrWhiteSpace(widget.Id))
                {
                    continue;
                }

                _widgets[widget.Id] = widget;
            }
            catch (Exception ex)
            {
                Log.Error($"Failed to instantiate widget {type.FullName}", ex);
            }
        }

        Log.Info($"Registered {_widgets.Count} widgets: {string.Join(", ", _widgets.Keys.OrderBy(k => k))}");
    }

    public IReadOnlyList<IWidget> All => _widgets.Values.OrderBy(w => w.DisplayName).ToArray();

    public IReadOnlyList<IWidget> ByCategory(string category)
        => _widgets.Values.Where(w => w.Category.Equals(category, StringComparison.OrdinalIgnoreCase)).ToArray();

    public IWidget? Find(string id)
        => _widgets.TryGetValue(id, out IWidget? widget) ? widget : null;

    public IReadOnlyList<string> Categories => _widgets.Values
        .Select(w => w.Category)
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .OrderBy(c => c)
        .ToArray();
}