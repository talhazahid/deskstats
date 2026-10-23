using System.IO;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Extensions.DependencyInjection;
using SysDash.Core.Diagnostics;
using SysDash.Core.Models;
using SysDash.Core.Threading;
using SysDash.Layouts;
using SysDash.Monitoring;
using SysDash.SDK.Session;
using SysDash.SDK.WindowManagement;
using SysDash.Theming;

namespace SysDash.App;

/// <summary>
/// Composition root. Builds the DI container, applies the persisted theme,
/// starts the metrics service, materializes the active layout into the sidebar
/// and floating windows, and installs the tray icon. Shutdown disposes metrics,
/// session and layout controller (which persists the final layout state).
/// </summary>
public partial class App : Application
{
    public App()
    {
        // Tray-first app: only the tray "Exit" (or session end) shuts down —
        // closing the last window must not (P1: floating widgets, the optional
        // sidebar and dialogs all come and go while the app keeps running).
        ShutdownMode = ShutdownMode.OnExplicitShutdown;
    }

    private ServiceProvider? _provider;
    private AppSession? _session;
    private LayoutController? _layout;
    private TrayService? _tray;
    private IMetricsService? _metrics;
    private System.Threading.Mutex? _singleInstance;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Every DeskStats window (settings, dialogs, About) shows the DeskStats logo.
        EventManager.RegisterClassHandler(typeof(Window), FrameworkElement.LoadedEvent, new RoutedEventHandler((sender, _) =>
        {
            if (sender is Window { Icon: null } window && AppIcons.WindowIcon() is { } icon)
            {
                window.Icon = icon;
            }
        }));

        // One running copy only. A restart (after installing the sensor driver)
        // passes --restarted and waits for the previous process to let go.
        _singleInstance = new System.Threading.Mutex(true, "DeskStats_SingleInstance_Mutex", out bool createdNew);
        if (!createdNew)
        {
            bool restarted = e.Args.Contains("--restarted", StringComparer.OrdinalIgnoreCase);
            bool acquired = false;
            try
            {
                acquired = restarted && _singleInstance.WaitOne(TimeSpan.FromSeconds(10));
            }
            catch (System.Threading.AbandonedMutexException)
            {
                acquired = true;
            }

            if (!acquired)
            {
                Shutdown(0); // already running — it lives in the tray
                return;
            }
        }

        string dataDir = AppPaths.DataDirectory;
        Log.Configure(Path.Combine(dataDir, "logs"));
        UserDataGuard.SnapshotOnVersionChange(); // keep a copy of widgets/settings before a new version touches them
        StartupTrace.FlushPending();
        StartupTrace.Mark("OnStartup entered");

        DispatcherUnhandledException += (_, args) =>
        {
            Log.Error("Unhandled UI-thread exception", args.Exception);
            args.Handled = true;
        };
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            Log.Error("Unhandled exception", args.ExceptionObject as Exception);

        try
        {
            Bootstrap();
        }
        catch (Exception ex)
        {
            Log.Error("Startup failed", ex);
            MessageBox.Show($"{AppPaths.ProductName} failed to start.\n\n{ex.Message}", AppPaths.ProductName);
            Shutdown(1);
        }
    }

    private void Bootstrap()
    {
        StartupTrace.Mark("bootstrap begin");

        var settingsService = new AppSettingsService();
        AppSettings settings = settingsService.Settings;
        var marshaller = new ThreadMarshaller(Dispatcher.CurrentDispatcher);
        StartupTrace.Mark("settings + marshaller ready");

        var services = new ServiceCollection();
        services.AddSingleton(settingsService);
        services.AddSingleton(settings);
        services.AddSingleton(marshaller);
        services.AddSingleton<IThreadMarshaller>(marshaller);

        services.AddSingleton(new MetricsOptions
        {
            PollIntervalMs = settings.PollIntervalMs,
            IncludeGpu = settings.LoadGpu,
            IncludeDrives = settings.LoadDrives,
            IncludeNetwork = settings.LoadNetwork,
        });
        services.AddSingleton<IMetricsService, MetricsService>();
        services.AddSingleton<ISessionContext, AppSession>();
        services.AddSingleton<IThemeService, ThemeManager>();
        services.AddSingleton<ILayoutService, LayoutService>();
        services.AddSingleton<IWidgetRegistry, WidgetRegistry>();
        services.AddSingleton<IWidgetWindowService, WidgetWindowService>();
        services.AddSingleton<SidebarWindow>();
        services.AddSingleton<LayoutController>();
        services.AddSingleton<AutoStartService>();
        services.AddSingleton<TrayService>();

        _provider = services.BuildServiceProvider();
        StartupTrace.Mark("DI container built");

        // Layouts first: ActiveLayout must reflect the saved/last-session layout.
        ILayoutService layouts = _provider.GetRequiredService<ILayoutService>();
        layouts.EnsureDefaults();
        StartupTrace.Mark("layouts ensured");

        // Theme: the dark/light dictionaries are already merged in App.xaml;
        // applying the persisted kind swaps to the right one. Layout-declared
        // themes are (re)applied interactively when switching layouts from the
        // tray via LayoutController.ActivateLayout.
        var theme = _provider.GetRequiredService<IThemeService>();
        theme.Apply(Resources, settings.Theme.Equals("light", StringComparison.OrdinalIgnoreCase)
            ? ThemeKind.Light
            : ThemeKind.Dark);
        StartupTrace.Mark($"theme applied ({settings.Theme})");

        _metrics = _provider.GetRequiredService<IMetricsService>();
        StartupTrace.Mark("metrics resolved (LHM init inside ctor)");
        _metrics.Start();
        StartupTrace.Mark("metrics started");

        _session = (AppSession)_provider.GetRequiredService<ISessionContext>();
        StartupTrace.Mark("session bridge ready");

        Action<MetricsSnapshot>? firstSnapshot = null;
        firstSnapshot = _ =>
        {
            _session.SnapshotReceived -= firstSnapshot;
            StartupTrace.Mark("first snapshot reached the UI");
        };
        _session.SnapshotReceived += firstSnapshot;

        var sidebar = _provider.GetRequiredService<SidebarWindow>();
        MainWindow = sidebar;
        StartupTrace.Mark("sidebar constructed");

        void OnFirstRender(object? sender, EventArgs args)
        {
            sidebar.ContentRendered -= OnFirstRender;
            StartupTrace.Mark("sidebar first render");
        }

        sidebar.ContentRendered += OnFirstRender;

        _layout = _provider.GetRequiredService<LayoutController>();
        sidebar.AttachController(_layout);

        // Tray first: its menu is built lazily, so it only needs the controller
        // object, and the user gets an icon right away instead of after the
        // widgets have been created.
        _tray = _provider.GetRequiredService<TrayService>();
        StartupTrace.Mark("tray installed");

        // LayoutService.ActiveLayout is driven by layouts\active.txt (last used
        // layout, or a preset on first run), so it is the single startup source
        // of truth. settings.ActiveLayout is maintained as a mirror by the
        // controller for diagnostics/menus.
        _layout.ApplyActiveLayout();
        StartupTrace.Mark("layout materialized");

        if (!_layout.HasVisibleWidgets)
        {
            // Nothing to draw (empty layout / everything hidden): tell the user the
            // app is alive instead of looking like it failed to start.
            _tray.ShowHint("DeskStats is running in the tray. Right-click the icon → Add widget to place one on your desktop.");
        }

        // Registry reconciliation is not needed for the first frame.
        Dispatcher.BeginInvoke(
            DispatcherPriority.ApplicationIdle,
            new Action(() =>
            {
                _provider?.GetRequiredService<AutoStartService>().Apply();
                StartupTrace.Mark("autostart applied");
            }));

        // P1: the sidebar is optional — Materialize() has already shown or
        // hidden it based on its docked content (floating-first), so there is
        // deliberately no unconditional Show() here.
        StartupTrace.Mark("startup sequence complete");
        // CPU sensors need the PawnIO driver: offer it once, and restart DeskStats
        // automatically after it was installed so the sensors appear.
        SensorDriver.Installed += () => Dispatcher.BeginInvoke(new Action(RestartApp));
        if (!PawnIOInstaller.IsPackaged && !SensorDriver.IsPawnIOInstalled && !settings.SensorDriverChoiceMade)
        {
            Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() =>
            {
                var dialog = new SensorSetupWindow();
                // Save only after the window opens; cancellation or closing means "continue".
                dialog.Loaded += (_, _) =>
                {
                    settings.SensorDriverChoiceMade = true;
                    settingsService.Save();
                };
                dialog.ShowDialog();
            }));
        }

        Log.Info($"DeskStats started — layout '{_layout.CurrentLayoutName}'.");
    }

    /// <summary>Starts a fresh copy (elevated, like this one) and exits this one.</summary>
    internal void RestartApp()
    {
        try
        {
            string? exe = Environment.ProcessPath;
            if (exe is not null)
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(exe, "--restarted")
                {
                    UseShellExecute = true,
                });
            }
        }
        catch (Exception ex)
        {
            Log.Error("Restart failed", ex);
            return;
        }

        _layout?.Dispose();
        Shutdown();
    }

    protected override void OnSessionEnding(SessionEndingCancelEventArgs e)
    {
        _layout?.Dispose();
        base.OnSessionEnding(e);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        var watch = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            // 1) Make the app disappear immediately: tray icon + every window.
            _tray?.Dispose();
            _tray = null;
            foreach (Window window in Windows.Cast<Window>().ToArray())
            {
                try
                {
                    window.Hide();
                }
                catch
                {
                    // already closed
                }
            }

            // 2) Persist state, then tear down. The layout controller saves the
            // final config; metrics stop their timer and close LHM off-thread.
            _metrics?.Stop();
            _layout?.Dispose();
            _session?.Dispose();
            _metrics?.Dispose();
            _provider?.Dispose();
        }
        catch (Exception ex)
        {
            Log.Error("Error during shutdown", ex);
        }

        Log.Info($"DeskStats exited cleanly ({watch.ElapsedMilliseconds} ms shutdown).");

        try
        {
            _singleInstance?.ReleaseMutex();
        }
        catch
        {
            // not owned (second instance exiting)
        }

        base.OnExit(e);
    }
}
