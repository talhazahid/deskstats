using SysDash.SDK.Widgets;

namespace SysDash.App;

/// <summary>Registry of widget types loaded from the SysDash.Widgets assembly.</summary>
public interface IWidgetRegistry
{
    IReadOnlyList<IWidget> All { get; }

    IReadOnlyList<IWidget> ByCategory(string category);

    IWidget? Find(string id);

    IReadOnlyList<string> Categories { get; }
}