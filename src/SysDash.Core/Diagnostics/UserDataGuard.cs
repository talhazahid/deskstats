using System.Reflection;

namespace SysDash.Core.Diagnostics;

/// <summary>
/// Keeps the user's widgets and settings safe across installs and updates.
/// Everything lives in %LocalAppData%\DeskStats, which the installer and
/// uninstaller never touch, so a reinstall or update comes back with the same
/// widgets. This class adds the safety nets:
///  - atomic writes (a crash or power cut can't leave a half-written file),
///  - a .bak copy of the last good file, used if the main file is unreadable,
///  - a snapshot of settings + layouts the first time a new version starts.
/// </summary>
public static class UserDataGuard
{
    /// <summary>Writes via a temp file so the target is never left half-written; keeps the previous copy as .bak.</summary>
    public static void WriteAllTextAtomic(string path, string content)
    {
        string tmp = path + ".tmp";
        File.WriteAllText(tmp, content);
        if (File.Exists(path))
        {
            try
            {
                File.Replace(tmp, path, path + ".bak", ignoreMetadataErrors: true);
                return;
            }
            catch (IOException)
            {
                // Fall through to a plain overwrite.
            }
        }

        File.Move(tmp, path, overwrite: true);
    }

    /// <summary>Text of the file, or of its .bak when the file is missing/unreadable by <paramref name="isValid"/>.</summary>
    public static string? ReadWithBackup(string path, Func<string, bool> isValid)
    {
        foreach (string candidate in new[] { path, path + ".bak" })
        {
            try
            {
                if (!File.Exists(candidate))
                {
                    continue;
                }

                string text = File.ReadAllText(candidate);
                if (isValid(text))
                {
                    if (candidate != path)
                    {
                        Log.Warn($"Restored {Path.GetFileName(path)} from its backup copy.");
                    }

                    return text;
                }

                if (candidate == path)
                {
                    // Keep the unreadable file for inspection instead of silently overwriting it.
                    File.Copy(path, path + ".corrupt", overwrite: true);
                }
            }
            catch (Exception ex)
            {
                Log.Warn($"Could not read {candidate}: {ex.Message}");
            }
        }

        return null;
    }

    /// <summary>
    /// First start of a new app version: copy settings.json + layouts into
    /// backups\before-v{version}, so an update can never lose the user's setup.
    /// Keeps the 5 most recent snapshots.
    /// </summary>
    public static void SnapshotOnVersionChange()
    {
        try
        {
            string dir = AppPaths.DataDirectory;
            string current = Assembly.GetEntryAssembly()?.GetName().Version?.ToString() ?? "0";
            string marker = Path.Combine(dir, "version.txt");
            string previous = File.Exists(marker) ? File.ReadAllText(marker).Trim() : string.Empty;
            if (previous == current)
            {
                return;
            }

            bool hasData = File.Exists(Path.Combine(dir, "settings.json")) ||
                           Directory.Exists(Path.Combine(dir, "layouts"));
            if (hasData)
            {
                string target = Path.Combine(dir, "backups", $"{DateTime.Now:yyyyMMdd-HHmmss}-from-{(previous.Length > 0 ? previous : "unknown")}");
                Directory.CreateDirectory(target);
                string settings = Path.Combine(dir, "settings.json");
                if (File.Exists(settings))
                {
                    File.Copy(settings, Path.Combine(target, "settings.json"), overwrite: true);
                }

                string layouts = Path.Combine(dir, "layouts");
                if (Directory.Exists(layouts))
                {
                    Directory.CreateDirectory(Path.Combine(target, "layouts"));
                    foreach (string file in Directory.GetFiles(layouts))
                    {
                        File.Copy(file, Path.Combine(target, "layouts", Path.GetFileName(file)), overwrite: true);
                    }
                }

                string backups = Path.Combine(dir, "backups");
                foreach (string old in Directory.GetDirectories(backups).OrderByDescending(d => d).Skip(5))
                {
                    Directory.Delete(old, recursive: true);
                }

                Log.Info($"Backed up user data before moving from '{previous}' to '{current}'.");
            }

            File.WriteAllText(marker, current);
        }
        catch (Exception ex)
        {
            Log.Warn($"Version snapshot skipped: {ex.Message}");
        }
    }
}
