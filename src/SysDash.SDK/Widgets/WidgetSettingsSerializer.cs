using System.Text.Json;
using SysDash.Core.Serialization;

namespace SysDash.SDK.Widgets;

/// <summary>Round-trips a concrete WidgetSettings subclass as JSON without polymorphic serialization.</summary>
public static class WidgetSettingsSerializer
{
    public static string Serialize(WidgetSettings settings)
        => JsonSerializer.Serialize(settings, settings.GetType(), JsonDefaults.Options);

    public static WidgetSettings? Deserialize(Type settingsType, string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        return (WidgetSettings?)JsonSerializer.Deserialize(json, settingsType, JsonDefaults.Options);
    }
}