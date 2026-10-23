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

    public static async Task CheckAsync(bool manual, Action saveState, Action exit)
    {
        if (PawnIOInstaller.IsPackaged)
        {
            if (manual) MessageBox.Show("This edition receives updates through Microsoft Store.", "DeskStats updates");
            return;
        }
        if (!await Gate.WaitAsync(0)) return;
        string? download = null;
        try
        {
            using var handler = new HttpClientHandler { AllowAutoRedirect = false };
            using var client = new HttpClient(handler) { Timeout = TimeSpan.FromMinutes(5) };
            client.DefaultRequestHeaders.UserAgent.ParseAdd("DeskStats/" + Assembly.GetExecutingAssembly().GetName().Version);
            var release = Parse(await client.GetStringAsync("https://talhazahid.github.io/deskstats/updates.json"));
            var current = Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0, 0);
            var latest = Version.Parse(release.Version);
            if (new Version(latest.Major, latest.Minor, Math.Max(0, latest.Build)) <= new Version(current.Major, current.Minor, Math.Max(0, current.Build)))
            {
                if (manual) MessageBox.Show("You have the latest version of DeskStats.", "DeskStats updates");
                return;
            }
            string folder = Path.Combine(AppPaths.DataDirectory, "updates", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            download = Path.Combine(folder, "DeskStats-Setup-" + release.Version + ".exe");
            using var response = await client.GetAsync(release.Url, HttpCompletionOption.ResponseHeadersRead);
            response.EnsureSuccessStatusCode();
            if (response.Content.Headers.ContentLength > 250 * 1024 * 1024) throw new InvalidDataException("Update is larger than expected.");
            await using (var input = await response.Content.ReadAsStreamAsync())
            await using (var output = File.Create(download))
            {
                byte[] buffer = new byte[81920];
                long total = 0;
                int count;
                while ((count = await input.ReadAsync(buffer)) > 0)
                {
                    total += count;
                    if (total > 250 * 1024 * 1024) throw new InvalidDataException("Update is larger than expected.");
                    await output.WriteAsync(buffer.AsMemory(0, count));
                }
            }
            await using (var file = File.OpenRead(download))
                if (!Convert.ToHexString(await SHA256.HashDataAsync(file)).Equals(release.Sha256, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("Update checksum does not match. Installation was stopped.");
            if (MessageBox.Show($"DeskStats {release.Version} is downloaded and ready. Install and restart now? Your widgets and positions will be kept. Windows may request administrator approval.",
                "DeskStats update", MessageBoxButton.YesNo, MessageBoxImage.Information) != MessageBoxResult.Yes) return;
            saveState();
            using var process = Process.Start(new ProcessStartInfo(download, "/SP- /VERYSILENT /SUPPRESSMSGBOXES /NORESTART /UPDATEAPP=1")
            { UseShellExecute = true, Verb = "runas" });
            if (process is null) throw new InvalidOperationException("The update installer could not start.");
            download = null; // Installer still needs the file; retain it until later cleanup.
            exit();
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            if (manual) MessageBox.Show("Update canceled. DeskStats will keep running.", "DeskStats updates");
        }
        catch (Exception ex)
        {
            Log.Warn("Update check failed: " + ex.Message);
            if (manual) MessageBox.Show("Could not update DeskStats. " + ex.Message, "DeskStats updates");
        }
        finally
        {
            if (download is not null) { try { File.Delete(download); } catch (IOException) { } }
            Gate.Release();
        }
    }
}
