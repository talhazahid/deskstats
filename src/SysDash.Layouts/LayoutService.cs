using System.Text.Json;
using SysDash.Core.Diagnostics;
using SysDash.Core.Serialization;
using SysDash.Layouts.Models;

namespace SysDash.Layouts;

/// <summary>
/// File-backed layout store. One JSON file per saved layout
/// (%LocalAppData%\SysDash\layouts\&lt;slug&gt;.json); the active layout name
/// lives in active.txt. Presets seed names until a file overwrites them.
/// </summary>
public sealed class LayoutService : ILayoutService
{
    private readonly string _directory;
    private readonly object _gate = new();
    private Layout _active;
    private string _activeName = LayoutPresets.Default;

    public LayoutService(string? storageDirectory = null)
    {
        _directory = storageDirectory ?? Path.Combine(SysDash.Core.Diagnostics.AppPaths.DataDirectory, "layouts");
        _active = LayoutPresets.Build(LayoutPresets.Default);
    }

    public string StorageDirectory => _directory;

    public Layout ActiveLayout
    {
        get
        {
            lock (_gate)
            {
                return _active.Clone();
            }
        }
    }

    public IReadOnlyList<string> LayoutNames
    {
        get
        {
            lock (_gate)
            {
                var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (string preset in LayoutPresets.Names)
                {
                    names.Add(preset);
                }

                foreach (string filePath in FileNames())
                {
                    names.Add(ReadName(filePath));
                }

                return names.OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToArray();
            }
        }
    }

    public IReadOnlyList<string> PresetNames => LayoutPresets.Names;

    public event EventHandler? ActiveLayoutChanged;

    public void EnsureDefaults()
    {
        Directory.CreateDirectory(_directory);

        lock (_gate)
        {
            string activeFile = Path.Combine(_directory, "active.txt");
            if (File.Exists(activeFile))
            {
                try
                {
                    string stored = File.ReadAllText(activeFile).Trim();
                    if (stored.Length > 0)
                    {
                        _activeName = stored;
                    }
                }
                catch (Exception ex)
                {
                    Log.Warn($"Could not read active layout file: {ex.Message}");
                }
            }

            Layout? loaded = LoadFromDisk(_activeName);
            if (loaded is not null)
            {
                _active = loaded;
            }
            else
            {
                Layout? preset = LayoutPresets.TryGet(_activeName);
                _active = preset ?? LayoutPresets.Build(LayoutPresets.Default);
            }
        }
    }

    public Layout? Get(string name)
    {
        lock (_gate)
        {
            return LoadFromDisk(name) ?? LayoutPresets.TryGet(name)?.Clone();
        }
    }

    public bool Activate(string name)
    {
        lock (_gate)
        {
            Layout? layout = LoadFromDisk(name) ?? LayoutPresets.TryGet(name);
            if (layout is null)
            {
                return false;
            }

            _active = layout;
            _activeName = name;
            WriteActiveName(name);
            Log.Info($"Activated layout '{name}' ({layout.Items.Count} items)");
        }

        ActiveLayoutChanged?.Invoke(this, EventArgs.Empty);
        return true;
    }

    public void SaveActive(Layout layout)
    {
        lock (_gate)
        {
            if (string.IsNullOrWhiteSpace(layout.Name))
            {
                layout.Name = _activeName;
            }

            _active = layout.Clone();
            _activeName = layout.Name;
            WriteFile(layout);
            WriteActiveName(layout.Name);
            Log.Info($"Saved active layout '{layout.Name}'");
        }

        ActiveLayoutChanged?.Invoke(this, EventArgs.Empty);
    }

    public void SaveAs(Layout layout, string name)
    {
        if (string.IsNullOrWhiteSpace(name) ||
            LayoutPresets.IsPreset(name))
        {
            throw new ArgumentException("Layout name must be non-empty and must not collide with a preset.", nameof(name));
        }

        layout.Name = name;
        layout.Id = name.ToLowerInvariant();

        lock (_gate)
        {
            WriteFile(layout);
            Log.Info($"Saved layout as '{name}'");
        }
    }

    public bool Delete(string name)
    {
        if (LayoutPresets.IsPreset(name))
        {
            return false;
        }

        lock (_gate)
        {
            string path = FilePathFor(name);
            if (!File.Exists(path))
            {
                return false;
            }

            File.Delete(path);
            if (_activeName.Equals(name, StringComparison.OrdinalIgnoreCase))
            {
                _active = LayoutPresets.Build(LayoutPresets.Default);
                _activeName = LayoutPresets.Default;
                WriteActiveName(LayoutPresets.Default);
            }
        }

        Log.Info($"Deleted layout '{name}'");
        return true;
    }

    // ---- internals --------------------------------------------------------

    private Layout? LoadFromDisk(string name)
    {
        string path = FilePathFor(name);
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            // Falls back to the .bak copy when the file is damaged.
            string? text = SysDash.Core.Diagnostics.UserDataGuard.ReadWithBackup(path, t =>
            {
                Layout? l = JsonSerializer.Deserialize<Layout>(t, JsonDefaults.Options);
                return l is not null && !string.IsNullOrWhiteSpace(l.Name);
            });
            if (text is null)
            {
                return null;
            }

            Layout? layout = JsonSerializer.Deserialize<Layout>(text, JsonDefaults.Options);
            return layout?.Clone();
        }
        catch (Exception ex)
        {
            Log.Error($"Failed to read layout file {path}", ex);
            return null;
        }
    }

    private void WriteFile(Layout layout)
    {
        Directory.CreateDirectory(_directory);
        string json = JsonSerializer.Serialize(layout, JsonDefaults.Options);
        SysDash.Core.Diagnostics.UserDataGuard.WriteAllTextAtomic(FilePathFor(layout.Name), json);
    }

    private void WriteActiveName(string name)
    {
        try
        {
            File.WriteAllText(Path.Combine(_directory, "active.txt"), name);
        }
        catch (Exception ex)
        {
            Log.Warn($"Could not write active layout name: {ex.Message}");
        }
    }

    private string FilePathFor(string name)
    {
        string slug = Sanitize(name);
        return Path.Combine(_directory, slug + ".json");
    }

    private IEnumerable<string> FileNames()
    {
        if (!Directory.Exists(_directory))
        {
            return Array.Empty<string>();
        }

        return Directory.EnumerateFiles(_directory, "*.json")
            .Where(f => !Path.GetFileName(f).Equals("active.txt", StringComparison.OrdinalIgnoreCase));
    }

    private static string ReadName(string filePath)
    {
        try
        {
            Layout? layout = JsonSerializer.Deserialize<Layout>(File.ReadAllText(filePath), JsonDefaults.Options);
            return layout?.Name ?? Path.GetFileNameWithoutExtension(filePath);
        }
        catch
        {
            return Path.GetFileNameWithoutExtension(filePath);
        }
    }

    private static string Sanitize(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var chars = name.Select(c => invalid.Contains(c) ? '_' : c).ToArray();
        string slug = new string(chars).Trim();
        return slug.Length == 0 ? "layout" : slug;
    }
}
