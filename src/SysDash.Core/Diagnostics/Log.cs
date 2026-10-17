using System.Diagnostics;

namespace SysDash.Core.Diagnostics;

/// <summary>
/// Minimal synchronous file + debug logger. No external dependencies, adequate
/// for a desktop utility. File output lives under %LocalAppData%\SysDash\logs\.
/// </summary>
public static class Log
{
    private static readonly object Sync = new();
    private static string? _filePath;

    /// <summary>True once <see cref="Configure"/> has set a log file.</summary>
    public static bool IsConfigured => _filePath is not null;

    public static void Configure(string logDirectory)
    {
        try
        {
            Directory.CreateDirectory(logDirectory);
            string suffix = DateTime.Now.ToString("yyyyMMdd");
            _filePath = Path.Combine(logDirectory, $"app-{suffix}.log");
        }
        catch
        {
            _filePath = null;
        }
    }

    public static void Info(string message) => Write("INFO ", message);

    public static void Debug(string message) => Write("DEBUG", message);

    public static void Warn(string message) => Write("WARN ", message);

    public static void Error(string message, Exception? ex = null)
        => Write("ERROR", ex is null ? message : $"{message}{Environment.NewLine}{ex}");

    private static void Write(string level, string message)
    {
        System.Diagnostics.Debug.WriteLine($"[SysDash {level}] {message}");

        if (_filePath is null)
        {
            return;
        }

        try
        {
            lock (Sync)
            {
                File.AppendAllText(_filePath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{level}] {message}{Environment.NewLine}");
            }
        }
        catch
        {
            // Logging must never take the app down.
        }
    }
}