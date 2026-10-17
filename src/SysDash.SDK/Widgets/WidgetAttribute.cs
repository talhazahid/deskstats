namespace SysDash.SDK.Widgets;

/// <summary>Declares widget metadata. Applied to IWidget implementations.</summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class WidgetAttribute : Attribute
{
    public WidgetAttribute(string id, string displayName)
    {
        Id = id;
        DisplayName = displayName;
    }

    public string Id { get; }

    public string DisplayName { get; }

    public string Description { get; set; } = string.Empty;

    public string Category { get; set; } = "system";
}