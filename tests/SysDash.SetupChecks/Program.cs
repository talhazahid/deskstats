using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using SysDash.App;
using SysDash.Core.Diagnostics;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        string output = Path.GetFullPath(args[0]);
        Directory.CreateDirectory(output);
        var checks = new Dictionary<string, bool>();
        try
        {
            var app = new App();
            app.InitializeComponent(); // Load the actual theme without bootstrapping monitoring.
            foreach (string theme in new[] { "Dark", "Light" })
            {
                app.Resources.MergedDictionaries[1].Source = new Uri($"pack://application:,,,/SysDash.Theming;component/Themes/{theme}.xaml");
                var window = new SensorSetupWindow();
                window.Show();
                window.UpdateLayout();
                var content = (FrameworkElement)window.Content;
                var bitmap = new RenderTargetBitmap((int)Math.Ceiling(content.ActualWidth), (int)Math.Ceiling(content.ActualHeight), 96, 96, PixelFormats.Pbgra32);
                bitmap.Render(content);
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(bitmap));
                using (var image = File.Create(Path.Combine(output, $"sensor-setup-{theme.ToLowerInvariant()}.png"))) encoder.Save(image);
                ((Button)window.FindName("ContinueButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                checks[$"ContinueCloses{theme}Dialog"] = !window.IsVisible;
            }
            string tampered = Path.Combine(output, "invalid-driver.bin");
            File.WriteAllText(tampered, "Not the pinned driver");
            checks["RejectsTamperedInstaller"] = !PawnIOInstaller.VerifyInstallerAsync(tampered).GetAwaiter().GetResult();
            checks["RejectsMissingInstaller"] = !PawnIOInstaller.VerifyInstallerAsync(tampered + ".missing").GetAwaiter().GetResult();
            if (args.Contains("--install-driver"))
            {
                if (SensorDriver.IsPawnIOInstalled) throw new InvalidOperationException("This test requires a clean runner without PawnIO.");
                string dependency = Path.Combine(AppContext.BaseDirectory, "Dependencies", "PawnIO_setup.exe");
                Directory.CreateDirectory(Path.GetDirectoryName(dependency)!);
                File.Copy(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "DeskStats", "Dependencies", "PawnIO_setup.exe"), dependency, true);
                checks["VerifiesBundledInstaller"] = PawnIOInstaller.VerifyInstallerAsync(dependency).GetAwaiter().GetResult();
                checks["ContinueDidNotInstallDriver"] = !SensorDriver.IsPawnIOInstalled;
                bool installedEvent = false;
                SensorDriver.Installed += () => installedEvent = true;
                var window = new SensorSetupWindow();
                window.Show();
                ((Button)window.FindName("InstallButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                var frame = new DispatcherFrame();
                var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
                DateTime deadline = DateTime.UtcNow.AddMinutes(5);
                timer.Tick += (_, _) => { if (!window.IsVisible || DateTime.UtcNow > deadline) frame.Continue = false; };
                timer.Start();
                Dispatcher.PushFrame(frame);
                timer.Stop();
                checks["ExplicitInstallRegisteredDriver"] = SensorDriver.IsPawnIOInstalled;
                checks["SuccessfulInstallNotifiedApp"] = installedEvent;
                checks["SuccessfulInstallClosedDialog"] = !window.IsVisible;
                window.Close();
            }
            File.WriteAllText(Path.Combine(output, "setup-checks.json"), JsonSerializer.Serialize(checks, new JsonSerializerOptions { WriteIndented = true }));
            return checks.Values.All(x => x) ? 0 : 1;
        }
        catch (Exception ex)
        {
            File.WriteAllText(Path.Combine(output, "error.txt"), ex.ToString());
            return 1;
        }
    }
}
