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
/// installs the bundled, verified PawnIO package on request.
/// </summary>
public static class SensorDriver
{
    public const string DownloadPage = "https://pawnio.eu/";

    /// <summary>Raised (on a worker thread) after PawnIO was installed successfully.</summary>
    public static event Action? Installed;

    [System.Runtime.InteropServices.DllImport("kernel32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    private static extern int GetCurrentPackageFullName(ref int length, System.Text.StringBuilder? name);

    /// <summary>
    /// True when running from an MSIX package (Microsoft Store build). Packaged apps cannot
    /// elevate or install drivers, so the "needs administrator / PawnIO" prompts are skipped.
    /// </summary>
    public static bool IsPackaged
    {
        get
        {
            try
            {
                int length = 0;
                // 15700 = APPMODEL_ERROR_NO_PACKAGE (not packaged).
                return GetCurrentPackageFullName(ref length, null) != 15700;
            }
            catch
            {
                return false;
            }
        }
    }

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
                string programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
                return service is not null && File.Exists(Path.Combine(programFiles, "PawnIO", "PawnIOLib.dll"));
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
            if (IsPackaged)
            {
                return !IsPawnIOInstalled
                    ? "Additional sensor readings need the PawnIO driver. You can install it from the tray menu."
                    : (!IsElevated ? "PawnIO is installed; some hardware readings may also require administrator permissions." : null);
            }

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
    /// Installs the bundled verified PawnIO package after an explicit request.
    /// Returns true only when installation completed and the driver is present.
    /// </summary>
    public static async Task<bool> InstallPawnIOAsync()
    {
        bool installed = await PawnIOInstaller.InstallAsync();
        if (installed) Installed?.Invoke();
        return installed;
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

}
