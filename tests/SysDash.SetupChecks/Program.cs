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
            // A neutral WPF host must not run DeskStats startup or touch the user's layout.
            var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            app.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("pack://application:,,,/SysDash.Theming;component/Themes/Base.xaml") });
            app.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("pack://application:,,,/SysDash.Theming;component/Themes/Dark.xaml") });
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
            if (args.Contains("--website-previews")) { WebsitePreviews.Render(output); return 0; }
            app.DispatcherUnhandledException += (_, e) =>
            {
                File.WriteAllText(Path.Combine(output, "dispatcher-error.txt"), e.Exception.ToString());
                e.Handled = true;
                Environment.Exit(1);
            };
            checks["PlacementScenariosPass"] = PlacementChecks.Run(output);
            checks["PowerScenariosPass"] = PowerChecks.Run(output);
            checks["WindowPersistenceScenariosPass"] = WindowPersistenceChecks.Run(output);
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
                // Render the actual WPF hover trigger without moving the user's pointer.
                var hoverKey = typeof(UIElement).GetField("IsMouseOverPropertyKey", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)?.GetValue(null) as DependencyPropertyKey;
                if (hoverKey is not null)
                {
                    var installButton = (Button)window.FindName("InstallButton");
                    installButton.SetValue(hoverKey, true);
                    window.UpdateLayout();
                    var hoverBitmap = new RenderTargetBitmap(bitmap.PixelWidth, bitmap.PixelHeight, 96, 96, PixelFormats.Pbgra32);
                    hoverBitmap.Render(content);
                    var hoverEncoder = new PngBitmapEncoder();
                    hoverEncoder.Frames.Add(BitmapFrame.Create(hoverBitmap));
                    using (var image = File.Create(Path.Combine(output, $"sensor-setup-{theme.ToLowerInvariant()}-hover.png"))) hoverEncoder.Save(image);
                    installButton.SetValue(hoverKey, false);
                }
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
                timer.Tick += (_, _) =>
                {
                    if (!window.IsVisible || ((Button)window.FindName("ContinueButton")).IsEnabled || DateTime.UtcNow > deadline)
                        frame.Continue = false;
                };
                timer.Start();
                Dispatcher.PushFrame(frame);
                timer.Stop();
                File.WriteAllText(Path.Combine(output, "driver-install-message.txt"), PawnIOInstaller.LastMessage);
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
