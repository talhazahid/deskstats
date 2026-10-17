using System.Diagnostics;
using System.Security.Principal;
using Microsoft.Win32;

namespace SysDash.Core.Diagnostics;

/// <summary>
/// Low-level sensor access status. LibreHardwareMonitor 0.9.5+ reads CPU
/// temperature / clocks / power (MSR, SMN, Super I/O) through the signed
/// **PawnIO** kernel driver — it replaced the old WinRing0 driver that
/// Windows Defender and other antiviruses now block. PawnIO is NOT bundled in
/// the library: if it is not installed, those sensors are simply empty on every
/// PC, even when the app runs as administrator. This helper detects that and
/// installs PawnIO on request (official package via winget).
/// </summary>
public static class SensorDriver
{
    public const string DownloadPage = "https://pawnio.eu/";

    /// <summary>Raised (on a worker thread) after PawnIO was installed successfully.</summary>
    public static event Action? Installed;

    public static bool IsElevated
    {
        get
        {
            try
            {
                using var identity = WindowsIdentity.GetCurrent();
                return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
            }
            catch
            {
                return false;
            }
        }
    }

    /// <summary>True when the PawnIO driver service is registered.</summary>
    public static bool IsPawnIOInstalled
    {
        get
        {
            try
            {
                using RegistryKey? service = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\PawnIO");
                if (service is not null)
                {
                    return true;
                }

                string programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
                return File.Exists(Path.Combine(programFiles, "PawnIO", "PawnIOLib.dll"));
            }
            catch
            {
                return false;
            }
        }
    }

    /// <summary>
    /// Plain-language reason why CPU sensors may be missing, or null when
    /// everything needed is in place.
    /// </summary>
    public static string? MissingReason
    {
        get
        {
            if (!IsElevated)
            {
                return "CPU temperature, clock and power need DeskStats to run as administrator.";
            }

            if (!IsPawnIOInstalled)
            {
                return "CPU temperature, clock and power need the free PawnIO sensor driver.";
            }

            return null;
        }
    }

    /// <summary>Logs elevation + driver status once at startup (helps support).</summary>
    public static void LogStatus()
        => Log.Info($"Sensor access: elevated={IsElevated}, PawnIO installed={IsPawnIOInstalled}");

    /// <summary>
    /// Installs PawnIO with winget (official package namazso.PawnIO). Falls back
    /// to opening the download page when winget is unavailable. Returns true when
    /// the driver is installed afterwards.
    /// </summary>
    public static async Task<bool> InstallPawnIOAsync()
    {
        if (IsPawnIOInstalled)
        {
            Installed?.Invoke();
            return true;
        }

        int exitCode = await Task.Run(() => RunHidden(
            "winget",
            "install --id namazso.PawnIO --exact --silent --accept-package-agreements --accept-source-agreements --disable-interactivity",
            TimeSpan.FromMinutes(5)));

        Log.Info($"PawnIO install via winget finished (exit {exitCode}).");

        if (IsPawnIOInstalled)
        {
            Installed?.Invoke();
            return true;
        }

        OpenDownloadPage();
        return false;
    }

    public static void OpenDownloadPage()
    {
        try
        {
            Process.Start(new ProcessStartInfo(DownloadPage) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Log.Warn($"Could not open {DownloadPage}: {ex.Message}");
        }
    }

    private static int RunHidden(string fileName, string arguments, TimeSpan timeout)
    {
        try
        {
            using Process? process = Process.Start(new ProcessStartInfo
            {
                FileName = fileName,
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

            // Drain output so the child never blocks on a full pipe.
            _ = process.StandardOutput.ReadToEndAsync();
            _ = process.StandardError.ReadToEndAsync();

            if (!process.WaitForExit((int)timeout.TotalMilliseconds))
            {
                return -2;
            }

            return process.ExitCode;
        }
        catch (Exception ex)
        {
            Log.Warn($"{fileName} failed: {ex.Message}");
            return -1;
        }
    }
}
