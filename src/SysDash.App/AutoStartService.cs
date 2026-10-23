using System.Diagnostics;
using Microsoft.Win32;
using SysDash.Core.Diagnostics;

namespace SysDash.App;

/// <summary>
/// "Start with Windows". SysDash runs elevated (LibreHardwareMonitor needs admin
/// for CPU temperature / clocks / power), and Windows silently skips elevated
/// programs listed in the HKCU Run key — so autostart is a Task Scheduler task
/// that runs at logon "with highest privileges" (no UAC prompt at logon).
/// The flag lives in <see cref="AppSettings.StartWithWindows"/>; <see cref="Apply"/>
/// reconciles the task with that flag (startup + settings edits).
/// </summary>
public sealed class AutoStartService
{
    private const string TaskName = "DeskStats";
    private const string LegacyTaskName = "SysDash";
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "SysDash";

    private readonly AppSettings _settings;
    private readonly AppSettingsService _settingsService;
    private readonly string _executablePath;

    public AutoStartService(AppSettings settings, AppSettingsService settingsService)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
        // Environment.ProcessPath is always populated, including single-file publishes.
        _executablePath = Environment.ProcessPath ?? string.Empty;
        if (_executablePath.Length == 0)
        {
            Log.Warn("AutoStartService: could not resolve the executable path; autostart will be disabled.");
        }
    }

    /// <summary>True when the SysDash logon task exists.</summary>
    public bool IsEnabled => RunSchtasks($"/Query /TN \"{TaskName}\"") == 0;

    /// <summary>Sets the flag in settings, applies it, and persists settings.</summary>
    public void SetEnabled(bool enabled)
    {
        _settings.StartWithWindows = enabled;
        Apply();
        _settingsService.Save();
    }

    /// <summary>Reconciles the logon task with the settings flag (runs in the background).</summary>
    public void Apply()
    {
        if (SensorDriver.IsPackaged)
        {
            // Microsoft Store (MSIX) build: autostart goes through the package's StartupTask.
            _ = ApplyPackagedAsync(_settings.StartWithWindows);
            return;
        }

        bool enable = _settings.StartWithWindows && _executablePath.Length > 0;
        string path = _executablePath;

        _ = Task.Run(() =>
        {
            try
            {
                RemoveLegacyRunKey();
                if (RunSchtasks($"/Query /TN \"{LegacyTaskName}\"") == 0)
                {
                    RunSchtasks($"/Delete /F /TN \"{LegacyTaskName}\"");
                }

                if (enable)
                {
                    int code = RunSchtasks(
                        $"/Create /F /TN \"{TaskName}\" /SC ONLOGON /RL HIGHEST /TR \"\\\"{path}\\\"\"");
                    if (code != 0)
                    {
                        Log.Warn($"AutoStartService: schtasks /Create failed (exit {code}).");
                    }
                }
                else if (RunSchtasks($"/Query /TN \"{TaskName}\"") == 0)
                {
                    RunSchtasks($"/Delete /F /TN \"{TaskName}\"");
                }
            }
            catch (Exception ex)
            {
                Log.Warn($"AutoStartService failed: {ex.Message}");
            }
        });
    }

    private static void RemoveLegacyRunKey()
    {
        try
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
            key?.DeleteValue(ValueName, throwOnMissingValue: false);
        }
        catch
        {
            // best effort
        }
    }

    private static int RunSchtasks(string arguments)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = "schtasks.exe",
                Arguments = arguments,
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            });

            if (process is null)
            {
                return -1;
            }

            process.WaitForExit(10_000);
            return process.HasExited ? process.ExitCode : -1;
        }
        catch
        {
            return -1;
        }
    }

    private static async Task ApplyPackagedAsync(bool enable)
    {
        try
        {
            Windows.ApplicationModel.StartupTask task =
                await Windows.ApplicationModel.StartupTask.GetAsync("DeskStatsStartup");
            if (enable)
            {
                await task.RequestEnableAsync();
            }
            else
            {
                task.Disable();
            }
        }
        catch (Exception ex)
        {
            Log.Warn($"AutoStart (packaged) failed: {ex.Message}");
        }
    }
}
