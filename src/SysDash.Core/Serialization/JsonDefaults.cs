using System.Text.Json;

namespace SysDash.Core.Serialization;

/// <summary>Shared System.Text.Json options: web (camelCase) naming, indented for human-readable files.</summary>
public static class JsonDefaults
{
    public static JsonSerializerOptions Options { get; } = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };
}