namespace SysDash.Core.Diagnostics;

/// <summary>
/// Product name + per-user data folder (%LocalAppData%\DeskStats). On first run
/// after the rename from "SysDash" the old settings and layouts are copied over,
/// so users keep their widgets.
/// </summary>
public static class AppPaths
{
    public const string ProductName = "DeskStats";

    private const string LegacyFolderName = "SysDash";

    private static readonly Lazy<string> Data = new(ResolveDataDirectory);

    public static string DataDirectory => Data.Value;

    private static string ResolveDataDirectory()
    {
        string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string current = Path.Combine(local, ProductName);
        string legacy = Path.Combine(local, LegacyFolderName);

        try
        {
            if (!Directory.Exists(current) && Directory.Exists(legacy))
            {
                CopyDirectory(legacy, current, skipFolder: "logs");
            }

            Directory.CreateDirectory(current);
        }
        catch
        {
            // Fall back to whatever exists; the app must still start.
        }

        return current;
    }

    private static void CopyDirectory(string source, string destination, string skipFolder)
    {
        Directory.CreateDirectory(destination);
        foreach (string file in Directory.GetFiles(source))
        {
            File.Copy(file, Path.Combine(destination, Path.GetFileName(file)), overwrite: false);
        }

        foreach (string dir in Directory.GetDirectories(source))
        {
            string name = Path.GetFileName(dir);
            if (!name.Equals(skipFolder, StringComparison.OrdinalIgnoreCase))
            {
                CopyDirectory(dir, Path.Combine(destination, name), skipFolder);
            }
        }
    }
}
