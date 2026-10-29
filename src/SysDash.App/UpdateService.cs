using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows;
using SysDash.Core.Diagnostics;

namespace SysDash.App;

public static class UpdateService
{
    private static readonly SemaphoreSlim Gate = new(1, 1);
    public sealed record Release(string Version, string Url, string Sha256);
    public static Release Parse(string json)
    {
        var release = JsonSerializer.Deserialize<Release>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new InvalidDataException("Missing update details.");
        if (!Version.TryParse(release.Version, out var version) || version.Major < 1 ||
            release.Sha256 is null || release.Sha256.Length != 64 || !release.Sha256.All(Uri.IsHexDigit) ||
            !Uri.TryCreate(release.Url, UriKind.Absolute, out var uri) || uri.Scheme != "https" ||
            uri.Host != "talhazahid.github.io" || uri.Query.Length != 0 || uri.Fragment.Length != 0 ||
            uri.AbsolutePath != $"/deskstats/downloads/DeskStats-Setup-{release.Version}.exe")
            throw new InvalidDataException("The update details could not be verified.");
        return release;
    }

    public static async Task<bool> CheckAsync(bool manual, Action saveState, Action exit, bool startup = false)
    {
        if (PawnIOInstaller.IsPackaged)
        {
            if (manual) MessageBox.Show("This edition receives updates through Microsoft Store.", "DeskStats updates");
            return false;
        }
        if (!await Gate.WaitAsync(0)) return false;
        string? download = null;
        var status = new UpdateWindow();
        status.Show();
        bool keepStatus = false;
        try
        {
            using var handler = new HttpClientHandler { AllowAutoRedirect = false };
            using var client = new HttpClient(handler) { Timeout = TimeSpan.FromMinutes(5) };
            client.DefaultRequestHeaders.UserAgent.ParseAdd("DeskStats/" + Assembly.GetExecutingAssembly().GetName().Version);
            using var checkTimeout = CancellationTokenSource.CreateLinkedTokenSource(status.DismissalToken);
            checkTimeout.CancelAfter(TimeSpan.FromSeconds(10));
            var release = Parse(await client.GetStringAsync("https://talhazahid.github.io/deskstats/updates.json", checkTimeout.Token));
            var current = Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0, 0);
            var latest = Version.Parse(release.Version);
            if (new Version(latest.Major, latest.Minor, Math.Max(0, latest.Build)) <= new Version(current.Major, current.Minor, Math.Max(0, current.Build)))
            {
                if (manual) status.ShowResult("You have the latest version of DeskStats.");
                keepStatus = manual;
                return false;
            }
            if (startup && !await status.AskBeforeStartupAsync(release.Version)) return false;
            string folder = Path.Combine(AppPaths.DataDirectory, "updates", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            download = Path.Combine(folder, "DeskStats-Setup-" + release.Version + ".exe");
            status.SetStatus($"Downloading DeskStats {release.Version}…");
            using var response = await client.GetAsync(release.Url, HttpCompletionOption.ResponseHeadersRead, status.DismissalToken);
            response.EnsureSuccessStatusCode();
            if (response.Content.Headers.ContentLength > 250 * 1024 * 1024) throw new InvalidDataException("Update is larger than expected.");
            await using (var input = await response.Content.ReadAsStreamAsync())
            await using (var output = File.Create(download))
            {
                byte[] buffer = new byte[81920];
                long total = 0;
                int count;
                while ((count = await input.ReadAsync(buffer, status.DismissalToken)) > 0)
                {
                    total += count;
                    if (total > 250 * 1024 * 1024) throw new InvalidDataException("Update is larger than expected.");
                    await output.WriteAsync(buffer.AsMemory(0, count));
                    double? percent = response.Content.Headers.ContentLength is > 0 ? total * 100.0 / response.Content.Headers.ContentLength.Value : null;
                    status.SetStatus($"Downloading DeskStats {release.Version}… {total / 1048576.0:0.0} MB", percent);
                }
            }
            status.SetStatus("Verifying the downloaded update…");
            await using (var file = File.OpenRead(download))
                if (!Convert.ToHexString(await SHA256.HashDataAsync(file)).Equals(release.Sha256, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("Update checksum does not match. Installation was stopped.");
            if (!startup && !await status.AskToInstallAsync(release.Version)) return false;
            status.DismissalToken.ThrowIfCancellationRequested();
            status.SetStatus("Starting the update installer…");
            saveState();
            string installLog = Path.Combine(folder, "install.log");
            Log.Info("Starting update " + release.Version + ". Installer log: " + installLog);
            using var process = Process.Start(new ProcessStartInfo(download, $"/SP- /VERYSILENT /SUPPRESSMSGBOXES /NORESTART /UPDATEAPP=1 /LOG=\"{installLog}\"")
            { UseShellExecute = true, Verb = "runas" });
            if (process is null) throw new InvalidOperationException("The update installer could not start.");
            download = null; // Installer still needs the file; retain it until later cleanup.
            exit();
            return true;
        }
        catch (OperationCanceledException)
        {
            Log.Info("Update check or download canceled/timed out; continuing startup.");
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            if (!startup) status.ShowResult("Update canceled. DeskStats will keep running.");
            keepStatus = !startup;
        }
        catch (Exception ex)
        {
            Log.Warn("Update check failed: " + ex.Message);
            if (!startup) status.ShowResult("Could not update DeskStats. " + ex.Message);
            keepStatus = !startup;
        }
        finally
        {
            if (!keepStatus) status.Finish();
            if (download is not null) { try { File.Delete(download); } catch (IOException) { } }
            Gate.Release();
        }
        return false;
    }
}
