# Migration Plan — SidebarDiagnostics → SysDash

This document is the result of inspecting the original
[SidebarDiagnostics](https://github.com/ArcadeRenegade/SidebarDiagnostics) source
(v1.3, `master` branch, .NET Framework 4.7.2) and planning the modernization.

## 1. What was inspected

| Area | Original file(s) | Notes |
|---|---|---|
| App shell / startup | `App.xaml.cs` | Tray icon, Squirrel updater, settings singleton, changelog, setup |
| Sidebar view | `Sidebar.xaml(.cs)`, `SidebarModel.cs` | AppBar-docked window, one global clock + monitor timers |
| Hardware monitoring | `Monitoring.cs` (≈3,500 lines) | `MonitorManager` → `MonitorPanel` → `iMonitor` → `iMetric`; LHM sensor selection; performance counters for drive/network; unit converters |
| Settings | `Settings.cs`, `SettingsModel.cs` | JSON singleton in `%LocalAppData%` |
| Window plumbing | `Windows.cs` (≈1,500 lines) | AppBar (`SHAppBarMessage`), multi-monitor + DPI, hotkeys, click-through, alt-tab exclusion, show-desktop hook, device-change hook |
| Graphs | `Graph.xaml`, `GraphModel.cs` | OxyPlot-based |
| Styling | `FlatStyle.xaml` | Mono flat brush suite |
| Packaging | csproj, Squirrel, `packages.config` | .NET Framework + packages.config era |

## 2. Reuse / refactor / rewrite assessment

### 2.1 Reusable (adapt, credit, port)
- **Sensor-selection heuristics** (`Monitoring.cs` `InitCPU/InitRAM/InitGPU`).
  High value, hardware-specific knowledge:
  - AMD CCD temperature: `"CCDs Max (Tdie)"`, then board `"CPU"` temp, then
    `"CPU Package"` / `"Tdie"`, fallback to first temperature sensor.
  - Core clock discovery via regex `^.*(CPU|Core).*#(\d+)$` over LHM sensor names.
  - GPU VRAM load from `"GPU Memory Used"` / `"GPU Memory Total"` data sensors,
    falling back to `Load` sensors.
  - Total CPU load = load sensor `Index == 0`; per-core loads = `Index > 0`.
- **Unit conversion layer** (`DataType`, `iConverter`, `C→F`, `MHz→GHz`,
  bits/s and bytes/s dynamic scaling, append-string table). Directly reusable.
- **Config-shape pattern**: a monitor is `Hardware[] + Metric[] + Param[]`.
  This becomes the per-widget settings model.
- **Window techniques** (`Windows.cs`): appbar messages, `GetDpiForMonitor`,
  `EnumDisplayMonitors`, `WS_EX_TRANSPARENT`, `WS_EX_TOOLWINDOW`,
  `DWMWA_EXCLUDED_FROM_PEEK`, hotkey registration. Reused in SysDash through a
  cleaner native-helper assembly (see 2.3).
- **PerformanceCounter sampling** for logical-disk and network bytes/sec.

### 2.2 Refactor (keep the idea, change the shape)
- **Observable metrics → value snapshots.** Original metrics are observable
  objects updated in-place on the UI thread. SysDash publishes *immutable
  `MetricsSnapshot` values* from a background collector. Widgets subscribe and
  project into their own view models. This is what makes independent widgets
  (and duplicate widgets) possible.
- **Singleton settings → DI-managed options objects**, persisted as JSON.
- **Manual `INotifyPropertyChanged`** boilerplate → small `ObservableObject` /
  `RelayCommand` primitives in `SysDash.Core`.
- **One sidebar Model → one `LayoutController`** that owns *instances* of
  widgets, spread across a sidebar shell and detached windows.
- **Global timer** → central `MetricsService` with configurable
  interval; subscribers pay only for what they render.

### 2.3 Rewrite (new design)
- **The widget model entirely.** SidebarDiagnostics hard-codes five panels
  (CPU/RAM/GPU/HD/Network) in XAML. SysDash defines a pluggable
  `IWidget` contract (`SysDash.SDK`) and each metric group is a self-contained
  widget (view + view model + settings), independently *instantiable*.
- **UI/presentation layer.** New Windows-11-style visual language:
  translucent glass panels, rounded corners, soft blur, large clean typography,
  sparklines. The old `FlatStyle.xaml` and OxyPlot graph windows are replaced.
- **Window management.** A dedicated `IWidgetWindowService` owns detached-widget
  window lifecycle instead of widgets manipulating Windows directly.
- **Layouts & themes as first-class persisted objects** (JSON) with presets.
- **Bootstrapping.** Composition root with dependency injection
  (`Microsoft.Extensions.DependencyInjection`).

## 3. Target framework & dependencies

| Concern | Decision | Rationale |
|---|---|---|
| Runtime | `.NET 8` (`net8.0-windows10.0.19041.0`) | LTS until Nov 2026; best WPF support on modern Windows |
| UI | **WPF retained** | Jacob's migration-path question, section 6 |
| MVVM | Hand-rolled `ObservableObject`/`RelayCommand` in Core | Zero extra deps; analogous to CommunityToolkit |
| DI | `Microsoft.Extensions.DependencyInjection` | Standard, small |
| Hardware | `LibreHardwareMonitorLib` (NuGet) | Same library as original; MPL-2.0, GPL-compatible |
| JSON | `System.Text.Json` | In-box, fast, no extra package |
| Tray | `Hardcodet.NotifyIcon.Wpf` | Same as original; works on .NET 8 |
| Charts | Custom `Sparkline` control in Theming | Tiny, dependency-free; OxyPlot overkill for compact sparklines |
| Network rates | `NetworkInterface` deltas (primary) | No admin/perf-counter dependency |
| Drive IO rates | `System.Diagnostics.PerformanceCounter` (optional, graceful fallback) | Preserves original behavior where counters exist |

## 4. Project structure (target)

```
SysDash/
  src/
    SysDash.Core        domain models, MVVM primitives, units, history buffer
    SysDash.SDK         IWidget contract, widget runtimes, window/host services
    SysDash.Monitoring  IMetricsService + LHM wrapper + snapshot projection
    SysDash.Theming     themes (dark/light), glass/acrylic helpers, Sparkline
    SysDash.Layouts     layout + placement models, JSON persistence, presets
    SysDash.Widgets     the 10 core widgets (views, VMs, settings)
    SysDash.App         composition root, shells, windows, tray, autostart
```

## 5. Migration phases (see docs/PLAN.md)

- **Phase 0** — docs & license compliance (this repo state)
- **Phase 1** — Core + SDK + Theming + Monitoring foundations
- **Phase 2** — Layouts persistence, presets
- **Phase 3** — App composition root, sidebar shell, detached windows, tray
- **Phase 4** — v1 widgets: CPU, RAM, Network, Clock
- **Phase 5** — remaining widgets: GPU, Drive, Weather, Timezone, Recycle bin, Media
- **Phase 6** — hardening: alerts, auto-hide, multi-monitor, packaging (MSIX/portable)

## 6. WPF vs migration-path tradeoffs

- **WPF (.NET 8):** fully supported, windowed/graphics model maps 1:1 to the
  original, mature `HwndSource` interop for the DWM/appbar tricks, smallest
  porting risk. Chosen.
- **Avalonia:** better JSON/XAML-island story & cross-platform, but its desktop
  glass/blur/edge-dock interop on Windows is less tooled and it would double
  the porting effort.
- **WinUI 3:** the most "native" look (acrylic for free), but the app lifecycle,
  window management, and tray/edge-dock story are still rough for a small
  desktop utility, and it constrains the SDK/widget hosting story to packaged
  apps. Revisit for a future v2 shell; the SDK contract keeps that door open.

## 7. Key tradeoffs / assumptions

1. **Administrator is no longer required.** Original README says "run as
   administrator." SysDash uses LHM in-process; LHM *may* need elevation for
   some sensors (fan/voltage) but degrades to whatever is readable. We wrap the
   entire hardware layer in fallbacks.
2. **Sidebar docking (v1) is edge-docked, not a full shell AppBar.** v1
   positions the window flush to the work-area edge, reserves nothing with
   `SHAppBarMessage` (avoids breaking other full-screen apps). The original
   `AppBarWindow` code is preserved under `docs/THIRD_PARTY` notes and can be
   re-attached in a later phase if "reserve space" behavior is required.
3. **Weather uses Open-Meteo** (keyless, free) with locality support; no user
   registration.
4. **Now Playing uses the Windows SMTC API** (`Windows.Media.Control`); on
   systems where media sessions are unavailable it shows an explicit "No media"
   state rather than failing.
5. **Widget settings are JSON dictionaries scoped per widget type** — new
   versions of a widget can add settings without breaking old layouts
   (unknown keys are ignored).