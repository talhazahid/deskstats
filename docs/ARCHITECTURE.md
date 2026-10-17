# SysDash — Target Architecture

## 1. High-level diagram (text)

```
                     ┌────────────────────────────────────────────────────┐
                     │                   SysDash.App                     │
                     │  Composition root (DI) | App.xaml.cs | tray       │
                     │  ┌────────────┐  ┌─────────────────────────────┐  │
                     │  │LayoutCtlrl │─▶│ SidebarWindow (edge dock)   │  │
                     │  └─────┬──────┘  │   ┌──┐ ┌──┐ ┌──┐ │ Items   │  │
                     │        │         └───┴──┴──┴──┴──┴─┴───────────┘  │
                     │        │ produces WidgetInstance runtime objects  │
                     │        ▼         ┌─────────────────────────────┐  │
                     │  ┌────────────┐  │ WidgetWindowService         │  │
                     │  │AppSession  │  │  ┌─────────────────────────┐│  │
                     │  └─────┬──────┘  │  │ WidgetWindow (floating) ││  │
                     │        │         │  │  ┌──────┐ ┌───────────┐ ││  │
                     │        │         │  │  │chrome│ │ widget    │ ││  │
                     │        │         │  │  └──────┘ │ view      │ ││  │
                     │        │         │  └───────────┴───────────┘ ││  │
                     └────────┼─────────┴─────────────────────────────┘──┘
                              │ Snapshot events (marshalled to UI thread)
   ┌──────────────────────────▼──────────────────────────────────────────┐
   │ SysDash.SDK (contracts)                                              │
   │  IWidget │ WidgetBase │ WidgetViewModel │ IWidgetHost               │
   │  IWidgetWindowService │ ISessionContext │ WidgetInstance            │
   └──────────────────────────┬──────────────────────────────────────────┘
                              │ consumption
   ┌──────────────────────────▼──────────────────────────────────────────┐
   │ SysDash.Widgets — implementations (views + VMs + settings)          │
   │  Cpu Ram Network Gpu Drive Clock Weather Timezone RecycleBin Media │
   └──────────────────────────┬──────────────────────────────────────────┘
                              │ reads MetricsSnapshot (Core), uses themes (Theming)
   ┌──────────────────────────▼──────────────────────────────────────────┐
   │ SysDash.Monitoring                                                  │
   │  MetricsService ── LHM Computer (Cpu/Gpu/Memory/Board)              │
   │  ├─ SensorCatalog: sensor-selection heuristics (adapted)            │
   │  ├─ DriveSampler: DriveInfo + optional perf counters                │
   │  └─ NetworkSampler: NetworkInterface deltas                         │
   │  publishes MetricsSnapshot on background timer                       │
   └──────────────────────────┬──────────────────────────────────────────┘
   │ SysDash.Theming: themes, acrylic/backdrop helpers, Sparkline, cards │
   │ SysDash.Layouts:  Layout / LayoutItem / WidgetPlacement, JSON +     │
   │                   presets; consumed by App and persisted on disk    │
   │ SysDash.Core:     MetricsSnapshot models, MVVM, units, HistoryBuffer│
   └─────────────────────────────────────────────────────────────────────┘
```

## 2. Data flow

1. `MetricsService` owns a `System.Threading.Timer`. Each tick it updates the
   LHM `Computer`, samples drives/network, and publishes one immutable
   `MetricsSnapshot` (Core models: `CpuReading`, `RamReading`, `GpuReading`,
   `DriveReading`, `NetworkAdapterReading`).
2. `AppSession` (App) subscribes, marshals to the UI thread, and re-raises via
   `ISessionContext.SnapshotReceived`.
3. Every active `WidgetViewModel` handles the snapshot, updates bindable
   properties, and pushes numeric values into its `HistoryBuffer` (sparkline).
4. `LayoutController` owns *instances*: it turns persisted `LayoutItem`s into
   `WidgetInstance` runtimes, hosts them in the sidebar shell or floating
   `WidgetWindow`s (via `IWidgetWindowService`), and persists any placement or
   settings change back to `ILayoutService` (JSON, debounced).

```
 timer tick ──▶ MetricsService ──▶ MetricsSnapshot ──▶ AppSession (UI thread)
                                                        │
                    ILayoutService ◀── LayoutController ─┤ ISessionContext
                    (JSON)              │                ▼
                          ┌─────────────┴────────────┐   WidgetViewModels
                          ▼                         ▼   (subscribe → update)
                  SidebarWindow (ItemsControl)  WidgetWindow[]
```

## 3. Project dependency graph (no cycles)

```
SysDash.Core          (no project references; System.* only)
SysDash.SDK           → Core
SysDash.Theming       → Core
SysDash.Monitoring    → Core
SysDash.Layouts       → Core
SysDash.Widgets       → Core, SDK, Theming
SysDash.App           → Core, SDK, Theming, Monitoring, Layouts, Widgets
```

All projects target `net8.0-windows10.0.19041.0` (see `Directory.Build.props`).

## 4. Widget contract (SysDash.SDK)

```csharp
public interface IWidget
{
    string Id { get; }                 // "cpu", "clock", ...
    string DisplayName { get; }
    string Description { get; }
    string Category { get; }           // hardware | time | internet | system
    WidgetSettings CreateDefaultSettings();
    WidgetSettings? DeserializeSettings(string json);
    string SerializeSettings(WidgetSettings settings);
    FrameworkElement CreateView(WidgetInstance instance);
}

public abstract class WidgetViewModel : ObservableObject, IDisposable
{
    // Activate(session) subscribes to snapshots (UI-thread marshalled)
    // Update(snapshot) is overridden per widget
    // Settings object is mutated via the per-widget settings UI
}
```

- `WidgetInstance` = one running widget (id, settings, view, view model).
- A widget type can be instantiated many times (e.g. two clocks, one per timezone).

## 5. Window management (dedicated service)

`IWidgetWindowService` (SDK) abstracted so widgets never touch `Window`:

```csharp
Guid CreateWindow(WidgetInstance instance, WidgetPlacementSpec placement);
void Close(Guid windowId);
bool TryGetHost(Guid windowId, out IWidgetHost host);
void BringToFront(Guid windowId);
void SetAlwaysOnTop(Guid windowId, bool top);
void SetClickThrough(Guid windowId, bool enabled);
void SetOpacity(Guid windowId, double opacity);
```

`WidgetPlacementSpec` carries position/size/opacity/always-on-top/
click-through/snap-grid/monitor index at creation time. The App implementation
owns `WidgetWindow` instances (translucent chrome, drag-to-move, bottom-right
resize grip, snap-to-grid while dragging tied to the persisted
`WidgetPlacement.SnapToGrid`, context menu). Closing a floating window removes
that widget instance from the layout (via `LayoutController`); the sidebar
card's right-click menu offers Detach/Settings/Remove instead.

## 6. Themes

`IThemeService` swaps a merged `ResourceDictionary` in `Application.Resources`.
Each widget uses `DynamicResource` brushes so theme switches apply live.
`WindowEffects` (Theming) applies Win11 backdrop (Mica/Acrylic via
`DWMWA_SYSTEMBACKDROP_TYPE`) with a Win10 `SetWindowCompositionAttribute`
fallback and graceful no-op degradation.

## 7. Persistence (all JSON, `%LocalAppData%\SysDash\`)

| File | Owner | Contents |
|---|---|---|
| `settings.json` | `AppSettingsService` | global app prefs (theme default, poll ms, autostart, sidebar edge/width/auto-hide/pin, sensor toggles) |
| `layouts/active.txt` | `ILayoutService` | name of the last-used layout (single startup source of truth) |
| `layouts/<name>.json` | `ILayoutService` | `Layout` = theme + `LayoutItem[]` (widget id, settings json, placement) |
| `logs/app-<date>.log` | `Log` (Core) | plain-text debug log |

Layouts are plain POCOs → export/import is copy/paste of one JSON file.

The App's `LayoutController` keeps ONE mutable working copy of the active
layout and mutates only that copy (all add/remove/detach/drag/settings edits),
persisting it debounced (500 ms) via `SaveActive`. `ActivateLayout` is the only
legal switch path — tray code never calls `ILayoutService.Activate` directly.

## 8. Robustness rules

- Every hardware access is wrapped: LHM `Computer` unavailable → degraded
  `MetricsSnapshot` with `null` readings; widgets render "—" instead of crashing.
- Every network/IO call (weather, external IP) has timeout + cache + fallback.
- Unhandled-exception handlers log and offer to continue running widgets.
- Widgets never talk to hardware directly; they only read snapshots.