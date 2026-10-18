using System.IO;
using System.Text.Json;
using SysDash.Core.Diagnostics;
using SysDash.Core.Serialization;

namespace SysDash.App;

/// <summary>Reads and writes settings.json in %LocalAppData%\SysDash\.</summary>
public sealed class AppSettingsService
{
    private readonly AppSettings _settings;

    public AppSettingsService(string? dataDirectory = null)
    {
        string dir = dataDirectory ?? AppPaths.DataDirectory;
        Directory.CreateDirectory(dir);
        FilePath = Path.Combine(dir, "settings.json");
        _settings = LoadFromDisk(FilePath);
    }

    public string FilePath { get; }

    public AppSettings Settings => _settings;

    public void Save()
    {
        try
        {
            UserDataGuard.WriteAllTextAtomic(FilePath, JsonSerializer.Serialize(_settings, JsonDefaults.Options));
        }
        catch (Exception ex)
        {
            Log.Error("Failed to save settings", ex);
        }
    }

    private static AppSettings LoadFromDisk(string path)
    {
        if (!File.Exists(path))
        {
            return new AppSettings();
        }

        try
        {
            string? text = UserDataGuard.ReadWithBackup(path, t => JsonSerializer.Deserialize<AppSettings>(t, JsonDefaults.Options) is not null);
            return text is null
                ? new AppSettings()
                : JsonSerializer.Deserialize<AppSettings>(text, JsonDefaults.Options) ?? new AppSettings();
        }
        catch (Exception ex)
        {
            Log.Warn($"Settings file invalid ({path}) — using defaults: {ex.Message}");
            return new AppSettings();
        }
    }
}
