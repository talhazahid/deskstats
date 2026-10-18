using System.ComponentModel;
using System.Diagnostics;
using System.Security.Cryptography;

namespace SysDash.Core.Diagnostics;

/// <summary>User-requested installation of the bundled, pinned official driver.</summary>
public static class PawnIOInstaller
{
    public const string ExpectedHash = "1F519A22E47187F70A1379A48CA604981C4FCF694F4E65B734AAA74A9FBA3032";
    private static readonly SemaphoreSlim Gate = new(1, 1);
    public static string LastMessage { get; private set; } = string.Empty;
    public static bool RestartRequired { get; private set; }
    public static async Task<bool> VerifyInstallerAsync(string path)
    {
        if (!File.Exists(path)) return false;
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(await SHA256.HashDataAsync(stream).ConfigureAwait(false))
            .Equals(ExpectedHash, StringComparison.OrdinalIgnoreCase);
    }
    [System.Runtime.InteropServices.DllImport("kernel32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    private static extern int GetCurrentPackageFullName(ref int length, System.Text.StringBuilder? name);
    public static bool IsPackaged
    {
        get
        {
            try { int length = 0; return GetCurrentPackageFullName(ref length, null) != 15700; }
            catch { return false; }
        }
    }

    public static async Task<bool> InstallAsync()
    {
        if (!await Gate.WaitAsync(0)) return false;
        try
        {
            RestartRequired = false;
            if (IsPackaged)
            {
                LastMessage = "This packaged edition cannot install the sensor driver. Continue with available readings.";
                return false;
            }
            if (SensorDriver.IsPawnIOInstalled) return true;
            string path = Path.Combine(AppContext.BaseDirectory, "Dependencies", "PawnIO_setup.exe");
            if (!File.Exists(path))
            {
                LastMessage = "The sensor installer is missing. Reinstall DeskStats, or download PawnIO from pawnio.eu. You can keep using limited readings.";
                return false;
            }
            if (!await VerifyInstallerAsync(path))
            {
                LastMessage = "The sensor installer could not be verified. Reinstall DeskStats before trying again.";
                return false;
            }
            LastMessage = "Installing the sensor driver…";
            using Process? process = Process.Start(new ProcessStartInfo(path, "-install -silent")
            {
                UseShellExecute = true,
                Verb = "runas",
                WindowStyle = ProcessWindowStyle.Hidden,
            });
            if (process is null)
            {
                LastMessage = "The sensor installer could not start. You can retry or continue with limited readings.";
                return false;
            }
            // Wait for the real process to finish; do not launch duplicate installers on a timeout.
            await process.WaitForExitAsync();
            Log.Info($"PawnIO user-requested install finished (exit {process.ExitCode}).");
            if (process.ExitCode == 3010)
            {
                RestartRequired = true;
                LastMessage = "The sensor driver needs a Windows restart. Save your work and restart Windows to enable sensor readings.";
                return false;
            }
            if (process.ExitCode != 0 || !SensorDriver.IsPawnIOInstalled)
            {
                LastMessage = $"Sensor driver installation did not complete (code {process.ExitCode}). You can retry or continue with limited readings.";
                return false;
            }
            LastMessage = "Sensor driver installed. DeskStats will restart to refresh readings.";
            return true;
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            LastMessage = "Installation was canceled. You can retry or continue with limited readings.";
            return false;
        }
        catch (Exception ex)
        {
            Log.Warn($"PawnIO installation failed: {ex.Message}");
            LastMessage = "The sensor driver could not be installed. You can retry or continue with limited readings.";
            return false;
        }
        finally { Gate.Release(); }
    }
}
