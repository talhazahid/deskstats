using System.Globalization;

namespace SysDash.Core.Diagnostics;

/// <summary>
/// Minimal synchronous file + debug logger. No external dependencies, adequate
/// for a desktop utility. File output lives under %LocalAppData%\DeskStats\logs\.
/// </summary>
public static class Log
{
    internal const int LogRetentionDays = 7;
    private const long DailySizeLimit = 10 * 1024 * 1024;
    private static readonly object Sync = new();
    private static string? _filePath;
    private static DateTime _fileDate;
    private static bool _lowPriorityCapped;

    /// <summary>True once <see cref="Configure"/> has set a log file.</summary>
    public static bool IsConfigured => _filePath is not null;

    public static void Configure(string logDirectory)
    {
        try
        {
            Directory.CreateDirectory(logDirectory);
            lock (Sync)
            {
                SwitchFile(logDirectory, DateTime.Now);
                QueueCleanup(logDirectory);
            }
        }
        catch
        {
            lock (Sync) { _filePath = null; }
        }
    }

    public static void Info(string message) => Write("INFO ", message);

    public static void Debug(string message) => Write("DEBUG", message);

    public static void Warn(string message) => Write("WARN ", message);

    public static void Error(string message, Exception? ex = null)
        => Write("ERROR", ex is null ? message : $"{message}{Environment.NewLine}{ex}");

    private static void Write(string level, string message, bool cleanupSummary = false)
    {
        System.Diagnostics.Debug.WriteLine($"[SysDash {level}] {message}");

        try
        {
            lock (Sync)
            {
                if (_filePath is null) return;
                DateTime now = DateTime.Now;
                if (now.Date != _fileDate)
                {
                    string directory = Path.GetDirectoryName(_filePath)!;
                    SwitchFile(directory, now);
                    QueueCleanup(directory);
                }
                if (!_lowPriorityCapped && File.Exists(_filePath))
                    _lowPriorityCapped = new FileInfo(_filePath).Length >= DailySizeLimit;
                if (!cleanupSummary && _lowPriorityCapped && (level == "DEBUG" || level == "INFO ")) return;
                File.AppendAllText(_filePath, $"{now:yyyy-MM-dd HH:mm:ss.fff} [{level}] {message}{Environment.NewLine}");
            }
        }
        catch
        {
            // Logging must never take the app down.
        }
    }

    private static void SwitchFile(string directory, DateTime now)
    {
        _fileDate = now.Date;
        _filePath = Path.Combine(directory, $"app-{now.ToString("yyyyMMdd", CultureInfo.InvariantCulture)}.log");
        _lowPriorityCapped = false;
    }

    private static void QueueCleanup(string directory)
    {
        try
        {
            _ = Task.Run(() =>
            {
                try
                {
                    int removed = CleanupOldLogs(directory, DateTime.Now, LogRetentionDays);
                    if (removed > 0) Write("INFO ", $"Log cleanup removed {removed} old log file(s).", cleanupSummary: true);
                }
                catch { /* Cleanup must never interrupt logging. */ }
            });
        }
        catch { /* Scheduling must never interrupt logging. */ }
    }

    internal static int CleanupOldLogs(string directory, DateTime now, int retentionDays)
    {
        int removed = 0;
        try
        {
            if (retentionDays < 0) return 0;
            foreach (string path in Directory.EnumerateFiles(directory, "app-*.log", SearchOption.TopDirectoryOnly))
            {
                try
                {
                    string name = Path.GetFileNameWithoutExtension(path);
                    DateTime date = DateTime.TryParseExact(name[4..], "yyyyMMdd", CultureInfo.InvariantCulture,
                        DateTimeStyles.None, out DateTime parsed) ? parsed : File.GetLastWriteTime(path);
                    if (date.Date >= now.Date.AddDays(-retentionDays)) continue;
                    // Synchronize the final check and deletion with writes and rollover.
                    lock (Sync)
                    {
                        if (_filePath is not null && string.Equals(Path.GetFullPath(path), Path.GetFullPath(_filePath), StringComparison.OrdinalIgnoreCase)) continue;
                        File.Delete(path);
                        removed++;
                    }
                }
                catch { /* A locked or inaccessible file must not prevent other cleanup. */ }
            }
        }
        catch { /* Missing or inaccessible directories are harmless. */ }
        return removed;
    }
}
