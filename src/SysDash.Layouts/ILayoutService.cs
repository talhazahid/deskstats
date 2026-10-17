using SysDash.Layouts.Models;

namespace SysDash.Layouts;

/// <summary>
/// Storage and activation of layouts (JSON in %LocalAppData%\SysDash\layouts).
/// Presets act as seeds: activating "Minimal" with no saved file yields the
/// preset; saving then attaches a user-editable file to that name.
/// </summary>
public interface ILayoutService
{
    /// <summary>Current layout as materialized POCO. Never null once EnsureDefaults ran.</summary>
    Layout ActiveLayout { get; }

    /// <summary>All available layout names (presets ∪ saved files).</summary>
    IReadOnlyList<string> LayoutNames { get; }

    /// <summary>Immutable preset templates.</summary>
    IReadOnlyList<string> PresetNames { get; }

    event EventHandler? ActiveLayoutChanged;

    /// <summary>Storage root (usually /layouts/). Exposed for diagnostics and docs.</summary>
    string StorageDirectory { get; }

    /// <summary>Creates the storage directory and a default layout when nothing exists.</summary>
    void EnsureDefaults();

    /// <summary>Layout by name: saved file first, preset as fallback. Null when unknown.</summary>
    Layout? Get(string name);

    /// <summary>Switches the active layout. Returns false when the name is unknown.</summary>
    bool Activate(string name);

    /// <summary>Persists the currently active layout under its Name (writes/overwrites a file).</summary>
    void SaveActive(Layout layout);

    /// <summary>Saves the given layout as a new named file (unless a preset name collides).</summary>
    void SaveAs(Layout layout, string name);

    /// <summary>Deletes a saved layout file. Presets cannot be deleted.</summary>
    bool Delete(string name);
}