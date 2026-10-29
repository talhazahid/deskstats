using System.IO;
using System.Text.Json;
using SysDash.Core.Diagnostics;

internal static class LogChecks
{
    public static bool Run(string output)
    {
        string directory = Path.Combine(output, "log-fixtures-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        DateTime now = new(2026, 10, 29, 12, 0, 0);
        string Put(string name, DateTime modified)
        {
            string path = Path.Combine(directory, name);
            File.WriteAllText(path, "fixture");
            File.SetLastWriteTime(path, modified);
            return path;
        }
        string expired = Put("app-20261021.log", now);
        string boundary = Put("app-20261022.log", now.AddYears(-1));
        string recent = Put("app-20261029.log", now.AddYears(-1));
        string future = Put("app-20261030.log", now.AddYears(-1));
        string fallbackOld = Put("app-invalid.log", now.AddDays(-8));
        string fallbackNew = Put("app-new.log", now);
        string unrelated = Put("other.log", now.AddYears(-1));
        string otherExtension = Put("app-20200101.txt", now.AddYears(-1));
        string nested = Path.Combine(directory, "nested");
        Directory.CreateDirectory(nested);
        string nestedLog = Path.Combine(nested, "app-20200101.log");
        File.WriteAllText(nestedLog, "fixture");
        int removed = Log.CleanupOldLogs(directory, now, Log.LogRetentionDays);
        var checks = new Dictionary<string, bool>
        {
            ["ExpiredFilenameAndFallbackRemoved"] = removed == 2 && !File.Exists(expired) && !File.Exists(fallbackOld),
            ["BoundaryRecentFutureAndRecentFallbackKept"] = File.Exists(boundary) && File.Exists(recent) && File.Exists(future) && File.Exists(fallbackNew),
            ["OnlyMatchingTopLevelLogsTouched"] = File.Exists(unrelated) && File.Exists(otherExtension) && File.Exists(nestedLog),
            ["MissingDirectoryIsHarmless"] = Log.CleanupOldLogs(Path.Combine(directory, "missing"), now, 7) == 0,
        };
        string activeDirectory = Path.Combine(directory, "active");
        Log.Configure(activeDirectory);
        Log.Info("active file fixture");
        string active = Path.Combine(activeDirectory, $"app-{DateTime.Now:yyyyMMdd}.log");
        checks["ActiveFileProtected"] = Log.CleanupOldLogs(activeDirectory, DateTime.Now.AddYears(2), 7) == 0 && File.Exists(active);
        using (var file = File.OpenWrite(active)) file.SetLength(10 * 1024 * 1024);
        long cappedLength = new FileInfo(active).Length;
        Log.Info("suppressed info");
        Log.Debug("suppressed debug");
        checks["SizeCapSuppressesInfoAndDebug"] = new FileInfo(active).Length == cappedLength;
        Log.Warn("retained warning");
        Log.Error("retained error");
        checks["SizeCapKeepsWarningsAndErrors"] = new FileInfo(active).Length > cappedLength;
        // Simulate crossing midnight without changing the system clock.
        const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static;
        typeof(Log).GetField("_fileDate", flags)!.SetValue(null, DateTime.Now.Date.AddDays(-1));
        typeof(Log).GetField("_filePath", flags)!.SetValue(null, Path.Combine(activeDirectory, "app-19990101.log"));
        File.WriteAllText(active, string.Empty);
        Log.Info("new day info");
        checks["RolloverSelectsTodayAndResetsCap"] = File.ReadAllText(active).Contains("new day info");
        File.WriteAllText(Path.Combine(output, "log-checks.json"), JsonSerializer.Serialize(checks, new JsonSerializerOptions { WriteIndented = true }));
        return checks.Values.All(value => value);
    }
}
