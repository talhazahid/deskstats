# Implementation Plan (phased)

Status icons: ✅ done in this scaffold · ⬜ not yet · 🚧 partial

## Phase 0 — Compliance & scaffolding (✅)
- [x] LICENSE (GPLv3) + NOTICE (attributions) + third-party license archive
- [x] `SysDash.sln`, `Directory.Build.props`, `global.json`, `.gitignore`
- [x] Migration plan / architecture / compliance docs

## Phase 1 — Foundations (✅)
- `SysDash.Core`: `ObservableObject`, `RelayCommand`, `HistoryBuffer`,
  `MetricsSnapshot` models, `UnitFormat`, `Log`, `IThreadMarshaller`,
  `JsonDefaults`.
- `SysDash.SDK`: `IWidget`, `WidgetBase`, `WidgetViewModel`, `WidgetInstance`,
  `WidgetSettings` + serializer, `WidgetAttribute`, `ISessionContext`,
  `IWidgetHost`, `IWidgetWindowService`, `WidgetWindowHandle`.
- `SysDash.Monitoring`: `IMetricsService`, `MetricsService` (background timer),
  `SensorCatalog` (adapted heuristics), `DriveSampler`, `NetworkSampler`,
  `MetricsOptions`.
- `SysDash.Theming`: `ThemeKind`, `IThemeService`/`ThemeManager`,
  `Dark.xaml`/`Light.xaml`/`Base.xaml`, `Sparkline` control, converters,
  `WindowEffects` (acrylic/backdrop/corners/click-through/topmost),
  monitor/DPI geometry.

## Phase 2 — Layouts (✅)
- `Layout`, `LayoutItem`, `WidgetPlacement`, `WorkspaceHost` enum
- `ILayoutService` + JSON `LayoutService` (index + per-layout files, atomic)
- Presets: `Minimal`, `Gaming`, `Work`, `Streaming`, `Default`

## Phase 3 — App shell & window services (✅)
- DI composition root, `App.xaml.cs` bootstrap (theme → metrics → layout → tray)
- `AppSession` (snapshot marshalling), `ThreadMarshaller`
- `SidebarWindow` (edge docked, auto-hide, click-through, width; card context
  menu: detach / settings / remove)
- `WidgetWindow` + `WidgetWindowService` (glass chrome, drag strip, resize grip,
  snap-to-grid, always-on-top / click-through / opacity)
- Tray icon (`Hardcodet.NotifyIcon.Wpf`, runtime-drawn) with add-widget /
  layout / theme / sidebar / per-instance / app-settings / about / exit menu
- `AutoStartService` (HKCU Run key), `AppSettingsService`, `AppSettingsWindow`,
  generic `SettingsEditorWindow`, `NameDialog`, `AboutWindow`
- `LayoutController` — single mutable working layout; materialize → instances →
  sidebar items/windows; add/duplicate/remove/detach; persist changes
  (500 ms debounced); `ActivateLayout` is the only switch path

## Phase 4 — Core widget set (✅)
CPU, RAM, Network, Clock:
- live metrics + sparklines + unit selection + graceful fallback
- per-widget settings (generic settings editor)

## Phase 5 — Full widget set (✅)
GPU, Drive, Weather (Open-Meteo), Timezone/world-clock, Recycle bin
(SHQueryRecycleBin), Now Playing (Windows.Media.Control SMTC best-effort).

## Phase 6 — Hardening & polish (⬜ for future work)
- qualifiers: alert flash / sound-toast thresholds per widget
- sidebar full AppBar reservation (port `AppBarWindow` from original)
- multi-monitor placement + DPI-change live repositioning
- packaging: MSIX + portable self-contained profiles
- weather geocoding UX, media-art rendering
- unit tests for `LayoutService`, `HistoryBuffer`, `UnitFormat`, converters
- localization (the original had resx; reintroduce after UI freeze)

## Suggested build order (commands)

```bash
cd SysDash
dotnet build SysDash.sln -c Release
dotnet run --project src/SysDash.App
```

First run creates `%LocalAppData%\SysDash\` and shows the default layout
(sidebar with clock/cpu/ram/network + one floating clock).