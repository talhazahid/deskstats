# SysDash — UI & Functional Reference: Architecture Audit

Status: **audit only — no code changed.**
Scope: the existing SysDash .NET 8 / WPF implementation (`SysDash/src/…`), audited against
`SYSDASH — DETAILED UI & FUNCTIONAL REFERENCE` (References 1–7).
Method: direct inspection of the implementation, plus the original SidebarDiagnostics source
for the legacy Hotkeys/Monitors tabs. Files inspected are listed per section.

---

## 1. Current architecture (verified from source)

### 1.1 Composition root & startup — `SysDash.App/App.xaml.cs`

Single DI container built in `Bootstrap()`. Registration and **startup order**:

| # | Step | Call |
|---|------|------|
| 1 | Settings load | `new AppSettingsService()` (`%LocalAppData%\SysDash\settings.json`) |
| 2 | DI container | `ServiceCollection` + `BuildServiceProvider()` |
| 3 | Layouts | `layouts.EnsureDefaults()` |
| 4 | Theme | `theme.Apply(Resources, settings.Theme)` |
| 5 | **Monitoring starts** | `_metrics.Start()` |
| 6 | Session | `AppSession` |
| 7 | Sidebar created | `SidebarWindow` → `MainWindow` |
| 8 | Layout materialised | `controller.ApplyActiveLayout()` → `Materialize()` (creates every widget view) |
| 9 | Autostart applied | `AutoStartService.Apply()` |
| 10 | **Tray created** | `TrayService` |
| 11 | Sidebar shown | `sidebar.Show()` |

`OnExit` disposes tray → layout controller (persists layout) → session → metrics → provider.

**Findings**

- **Single monitoring engine** (`MetricsService` is a singleton). ✅ Satisfies "do not start five
  copies of the hardware engine".
- **Tray icon is created *late*** (step 10) and the window is shown last (step 11). The reference
  (Ref. 7) wants the tray icon available "as early as practical". Tray creation mostly builds a
  `Hardware` menu, so moving it earlier is low-risk — but it currently depends on `LayoutController`
  state for its "Widgets" list, so it cannot simply move to step 3 without decoupling.
- **Widget views are all created during startup** (step 8). This is the most likely dominant startup
  cost alongside hardware init.
- **LHM initialisation is inside the metrics service** (`MetricsService.InitializeHardware()` →
  `computer.Open()`), which can take seconds on some machines. It runs on the sampler thread, not the
  UI thread, so it should not freeze the UI — **but this has never been measured.** The reference
  explicitly says "investigate the actual cause of slow startup before applying any optimization".
  → Action: instrument the 11 steps above before changing anything.
- **Weather does not block startup.** `WeatherViewModel` fetches via
  `_refreshTimer.Tick += (_,_) => _ = RefreshAsync()` with `HttpClient` + `await`; the timer only
  starts on `OnActivated()`. ✅ Satisfies "weather must not block CPU/GPU/RAM".

### 1.2 Widget presentation model — docked vs detached

`Layouts/Models/WidgetPlacement.cs` and `App/LayoutController.cs`:

- Every widget instance has a `WidgetPlacement`: `Docked`, `X`, `Y`, `Width`, `Height`, `Opacity`
  (0.05–1), `AlwaysOnTop`, `ClickThrough`, `SnapToGrid`, `MonitorIndex`.
- `Docked = true` → hosted as a card in `SidebarWindow` (`SidebarWindow.AddItem`).
- `Docked = false` → hosted in a `WidgetWindow` floating window (`LayoutController.HostDetached`).
- `AddWidget(widgetId, docked = true, …)` — **new widgets default to docked**, i.e. the sidebar is
  still the default container. The reference wants floating to be the primary model.
- `DetachInstance(id)` moves a docked card into a floating window; `DuplicateWidget(id)` clones.

**Findings**

- The floating-widget machinery **already exists** and is fairly complete (see 1.3/1.4) — this is the
  single biggest asset: the reference's "independent widgets" is a *defaults/UX* change, not a
  from-scratch rewrite.
- There is **no "hidden" state**: a widget is either present (docked/detached) or `RemoveInstance`d.
  The reference needs three states — **Not Added / Hidden / Visible** — and requires that **hiding
  does not stop monitoring**.

### 1.3 Sidebar shell — `SysDash.App/SidebarWindow.xaml(.cs)`

- Full **work-area-height** window docked to the left/right edge (`SnapToEdge` sets `Top = work.Top`,
  `Height = work.Height`), width from `SidebarWidth` (240–560).
- Acrylic/Mica backdrop, rounded corners, `WS_EX_TOOLWINDOW`.
- **Auto-hide** (`PollAutoHide`, 150 ms `DispatcherTimer`): collapses to an **8 px** strip and enables
  click-through (`WindowEffects.SetClickThrough`), expands when the cursor enters a hot zone.
- **No AppBar reservation** — it overlays the desktop; it does not reserve work area.
- Docked card context menu (`BuildCardMenu`): **Detach window / Settings… / Remove widget**.

**Findings (vs Ref. 1 problems 6–8)**

- "Full-height sidebar even when only one widget is enabled" — **confirmed**: height is always the
  full work area regardless of content.
- "Do not retain an invisible full-height window behind the widgets" — **currently safe**: the shell
  is either visible/opaque (expanded) or an 8 px click-through strip (collapsed). It is never a large
  invisible hit-test surface. This must be preserved when the sidebar becomes optional/removable.
- The reference wants to **remove the sidebar dependency**, not merely shrink it. Plan must decide
  whether the sidebar survives as an *optional* container or is deleted outright.

### 1.4 Floating widget window — `SysDash.App/WidgetWindow.cs`

- Borderless, `WS_EX_TOOLWINDOW`, `Topmost` from placement, window `Opacity`.
- Left-drag on an invisible top strip (`DragMove`) with grid snap (`SnapAxis`), bottom-right resize
  grip, `PlaceAt` clamps to the target monitor's work area.
- Persists position/size/flags back to the controller (`SetDetachedPosition/Size/Flags`).
- **Context menu** (`BuildContextMenu`): *Always on top · Click-through · Opacity (40/60/80/100%) ·
  Settings… · Remove widget*.

**Findings (vs Ref. 3.2)**

- Has: drag, resize, snap, always-on-top, click-through, opacity, per-widget settings, remove.
- Missing from the widget menu: **Appearance ▸** (theme/background colour/opacity/font family/font
  size/text colour/accent/UI scale), **Position ▸** as a group (Normal / On Desktop / Lock / Reset),
  **Refresh Widget**, **Manage All Widgets…**.
- `Ctrl + mouse wheel` → opacity (Ref. 5) is **not implemented**.
- "On desktop (behind windows)" z-order mode (Ref. 6 / original spec) is **not implemented** — only a
  `Topmost` boolean exists.

### 1.5 Tray menu — `SysDash.App/TrayService.cs`

Verified structure:

```
Add widget ▸ (categories → widgets from IWidgetRegistry)
Layouts ▸ (list + Save current as…)
Theme ▸ (Dark / Light)
Sidebar ▸ (Show/Hide sidebar, Auto-hide, Pin)
Widgets ▸ (per instance: Detach window, Settings…, Remove)
──
App settings…
About SysDash
Exit
```

**Findings (vs Ref. 2)** — matches the reference's description of the *current* app almost exactly,
including the "Sidebar" submenu the reference wants removed. Missing per the reference:
Hide existing widget · Restore hidden widget · Reset/restore widget positions · a Widget Manager
entry. The "Add widget" list is registry-driven, so it already "corresponds to the widgets actually
implemented" ✅.

### 1.6 Settings surfaces

| Window | File | Nature |
|---|---|---|
| App settings | `AppSettingsWindow.xaml(.cs)` | **Single non-tabbed form**: Monitoring (poll interval, GPU/drives/network), Appearance (**theme only**), Sidebar (edge, width, auto-hide, pin), Startup (autostart) |
| Widget settings | `SettingsEditorWindow.xaml(.cs)` | **Reflection-driven** form over the widget's settings POCO (bool→CheckBox, numeric→validated TextBox, string→TextBox, enum→ComboBox, list→comma TextBox) |

**Findings (vs Ref. 4/5)**

- The reference's target navigation (`General · Appearance · Widget Manager · Advanced · Hotkeys ·
  About`) has **no counterpart** — the current window is a flat 4-section form and overlaps the old
  "Advanced"+"Customize" content only partially.
- Present from the legacy app: poll interval ✅, autostart ✅, theme ✅ (new), opacity **per floating
  window only**, click-through **per widget only**.
- Missing from legacy that the reference says to preserve: **UI Scale**, **Language**,
  **default Screen/monitor preference**, **Show tray icon**, **Hide at startup**, and the whole
  **Customize** family (background colour/opacity, text align, font size/colour, alert colour,
  alert blink, show PC name, 24 h clock, date format).
- The reflection editor is a good **reuse** asset for widget-specific settings, but it is not a
  design-system form; the reference wants reusable Appearance/Position controls shared across widgets.

### 1.7 Theming / appearance — `SysDash.Theming/`

- `ThemeManager.Apply` swaps a merged `Dark.xaml` / `Light.xaml` palette on top of `Base.xaml`
  (brushes + `WidgetCard`/`WidgetTitle`/`WidgetLabel` styles + `UiFont` = "Segoe UI Variable Text,
  Segoe UI").
- Theme is **global**; a layout can also persist a `Theme` and re-apply it on activation
  (`LayoutController.ApplyLayoutTheme`).
- **Per-widget appearance does not exist** beyond window `Opacity`/`Topmost`/`ClickThrough`. There is
  no per-widget background colour, font family/size, text colour, accent colour, or UI scale.

**Finding**: Ref. 4.3's "global appearance + per-widget override" requires a **new appearance model**
and resolution pipeline (global default → widget override → window chrome). This is the largest new
subsystem in the reference.

### 1.8 Monitoring / session (must stay intact)

- One `MetricsService` singleton publishes immutable `MetricsSnapshot`s → `AppSession` marshals to the
  UI thread → `ISessionContext.SnapshotReceived` → widget VMs update on the UI thread.
- Monitoring toggles are **global** (`MetricsOptions`: `IncludeGpu/IncludeDrives/IncludeNetwork`, poll
  interval). Widget presence does not gate sampling.

**Finding**: the requirement "hiding a CPU widget must not disable CPU monitoring" is **already
structurally satisfied** — but a new *Hidden* state must be implemented as presentation-only so it
does not touch `MetricsOptions`. Do not couple visibility to the providers.

### 1.9 Layouts / persistence — `SysDash.Layouts/`

- `LayoutService` (JSON per layout + `layouts/active.txt`), presets, `LayoutController` holds ONE
  mutable working layout and debounces saves (500 ms). `LayoutItem` = `InstanceId`, `WidgetId`,
  `TitleOverride`, `SettingsJson`, `Placement`.

### 1.10 Hotkeys

- **Not implemented at all** (no `RegisterHotKey`, no `InputBinding`/`KeyBinding` anywhere).
- The original SidebarDiagnostics does have them. From its `Settings.xaml.cs` / resource strings the
  supported actions are: **Toggle, Show, Hide, Reload, Close, CycleEdge, CycleScreen**
  (`Hotkey.KeyAction`). `CycleEdge`/`CycleScreen` are sidebar-specific and should be re-mapped or
  dropped when there is no sidebar.

---

## 2. Gap analysis vs the reference

Legend: ✅ have · 🟡 partial · ❌ missing

### Ref. 1 — Sidebar → independent floating widgets
| Requirement | Status | Evidence / gap |
|---|---|---|
| Widgets as independent floating windows | 🟡 | `WidgetWindow` exists; **new widgets default to docked** |
| No fixed sidebar dependency | ❌ | Sidebar is `MainWindow` and always created |
| Weather (≈300×200) occupies only its own area | 🟡 | Works when detached; docked it lives in the full-height shell |
| No invisible full-height window behind widgets | ✅ | Collapsed shell = 8 px + click-through |
| Sidebar not full-height when only one widget | ❌ | Height = full work area always |

### Ref. 2 — Tray as central controller
| Requirement | Status |
|---|---|
| Add available widget | ✅ |
| Hide existing widget | ❌ |
| Restore hidden widget | ❌ |
| Remove active widget | ✅ (`Widgets ▸ Remove`) |
| Widget manager entry | ❌ |
| App settings | ✅ |
| Reset/restore widget positions | ❌ |
| Exit | ✅ |
| Remove "Sidebar" concept | ❌ (submenu still present) |

### Ref. 3.2 — Per-widget right-click menu
| Item | Status |
|---|---|
| Configure Widget… | ✅ (`Settings…`) |
| Appearance ▸ (theme/bg colour/opacity/font/size/text colour/accent/UI scale) | ❌ (only window opacity) |
| Position ▸ (Always on top / Normal / On desktop / Click through / Lock / Reset) | 🟡 (topmost + click-through only) |
| Refresh Widget | ❌ |
| Lock Position | 🟡 (placement flag `Locked` not present; snap only) |
| Hide Widget | ❌ |
| Remove Widget | ✅ |
| Manage All Widgets… | ❌ |
| Ctrl+wheel opacity (Ref. 5) | ❌ |

### Ref. 4 — Legacy settings → modern settings
| Legacy | Status | Notes |
|---|---|---|
| General: Run at startup | ✅ | `AutostartCheck` |
| General: Dock L/R | — | to be dropped per reference |
| General: Screen # | ❌ | no default-monitor preference |
| General: Language | ❌ | not implemented |
| Advanced: UI Scale | ❌ | (per-widget "size" also absent) |
| Advanced: Offsets | — | to be reconsidered |
| Advanced: Polling interval | ✅ | |
| Advanced: Show tray icon | ❌ | |
| Advanced: Click through | 🟡 | per floating window only |
| Advanced: Hide at startup | ❌ | |
| Customize: bg colour/opacity, text align, font size/colour, alert colour/blink, PC name, 24 h, date format | ❌ / 🟡 | mostly widget-specific today |
| Monitors tab → Widget Manager | ❌ | no Enabled/Visible/Not-Added UI |
| Hotkeys tab (Toggle/Show/Hide/Reload/Close/CycleEdge/CycleScreen) | ❌ | nothing implemented |

### Ref. 5/6/7
- Ref. 5 target nav (General/Appearance/Widget Manager/Advanced/Hotkeys/About): ❌ not present.
- Ref. 6 independent, draggable, no mandatory container: 🟡 attainable via detached mode, not the default.
- Ref. 7 startup: 🟡 tray created late; startup never measured; weather already non-blocking; single engine ✅.

---

## 3. Cross-cutting findings

1. **The hard part already exists.** Detached floating windows, per-monitor placement, drag/resize/snap,
   always-on-top/click-through/opacity, layouts and per-widget settings are implemented. The reference
   is mostly a **defaults + UX + settings-surface** change, plus one genuinely new subsystem (the
   **appearance model**).
2. **Missing conceptual state: Hidden.** Needed by Refs. 2 and 4.4, and must be presentation-only so
   monitoring is untouched.
3. **No per-widget appearance model.** Global theme exists; overrides do not. This is the biggest new
   build.
4. **Settings window needs restructuring**, and there is no Widget Manager UI.
5. **Hotkeys entirely absent** (legacy actions identified above).
6. **Startup order** puts the tray last; startup cost is unmeasured. Instrument first.
7. **Design system already centralised** in `Base.xaml` (brushes/styles/font) — good foundation for
   Ref. 5's "one unified design system".

---

## 4. Proposed target architecture (proposal — not implemented)

- **Presentation:** floating windows become the default. The sidebar becomes an **optional container**
  (or is removed) so it can no longer be a full-height dependency. Keep the click-through/auto-hide
  safety so no invisible full-height surface exists.
- **Widget lifecycle:** add a tri-state `WidgetState { NotAdded, Hidden, Visible }` to the layout model;
  `Hidden` keeps the instance (and monitoring) alive but creates no view.
- **Appearance model:** new `WidgetAppearance` record resolved as `global → widget override → chrome`,
  applied through the Theming layer; add reusable Appearance/Position menu builders and a font/scale
  provider so the reflection editor and settings window share one design system.
- **Widget context menu:** one reusable builder used by docked cards *and* floating windows, with
  Appearance ▸ / Position ▸ / Refresh / Lock / Hide / Remove / Manage All.
- **Tray:** drop the Sidebar submenu; add Hide/Restore, Reset positions, and **Manage All Widgets…**.
- **Settings window:** tabbed nav (General · Appearance · Widget Manager · Advanced · Hotkeys · About)
  reusing existing controls and styles; extend `AppSettings` with the preserved legacy options.
- **Hotkeys:** `RegisterHotKey`-based service with the legacy action set, re-mapped to widgets
  (e.g. Toggle widgets instead of CycleEdge/CycleScreen).
- **Monitoring:** unchanged — one engine, global sampling, never gated by visibility.
- **Startup:** instrument the 11 steps, then move tray creation earlier, and defer non-critical work.

### Phasing (agreed 2026-10-16)
1. **P0 — Instrument startup** and publish a baseline (no behaviour change). *(done — see
   [STARTUP-BASELINE.md](STARTUP-BASELINE.md))*
2. **P1 — Floating-first + Hidden state + tray changes** (remove Sidebar submenu; Hide/Restore/Reset).
   *Implemented 2026-10-16 — details:*
   - `WidgetPlacement.Hidden` (persisted): instance stays in the layout (settings/position/monitoring)
     but creates no view; tray *Widgets* lists hidden items with *Show* (and *Remove*) so they stay
     reachable.
   - New widgets **float by default** in a cascading on-screen position; `AddWidget(docked:)` now
     defaults to `false`.
   - The sidebar is **optional and derived**: it is shown iff it hosts ≥1 visible docked widget
     (`SyncSidebarVisibility`), never an empty full-height shell. Auto-hide is unaffected (it
     animates Width). Tray *Sidebar* submenu removed — those settings live in App Settings.
   - Tray *Widgets* per instance: Hide · Dock to sidebar/Detach window · Settings… · Remove; new
     top-level *Reset widget positions* (cascade recovery for off-screen floats). Dialogs are owned
     by the sidebar only while it is visible.
   - `ShutdownMode = OnExplicitShutdown` + sidebar close-guard (`AllowClose` only from tray Exit):
     closing windows can no longer quit the tray app, and the sidebar can always be shown again.
   - Fixed while verifying (latent since before P1): *Detach window* on a sidebar card threw
     `InvalidOperationException` ("element is already the logical child of another element") and the
     widget disappeared until restart. Both re-homing ends now release the View
     (`SidebarWindow.RemoveItem`, `WidgetWindow.Closed`) — the new *Dock to sidebar* relies on the
     same fix, in the opposite direction.
   - Also exposed by the new flags and fixed: `AutoStartService` no longer uses
     `Assembly.Location` as a fallback (IL3000 — always empty in single-file; `Environment.ProcessPath`
     is the real source).
   - Verified 2026-10-16 with layout-driven state tests (0 items / 2 floats + 1 hidden / 2 docked +
     1 hidden / mixed 1 docked + 1 float + 2 hidden): hidden items never materialize, the sidebar is
     shown exactly when visible docked content exists, empty layouts leave no shell on screen, and
     no startup/lifecycle errors are logged (the tray menu itself builds on open and was not
     exercised headlessly; the earlier `StartupTrace`-style runs in STARTUP-BASELINE.md remain valid).
3. **P2 — Reusable widget context menu** (Appearance ▸ / Position ▸ / Refresh / Lock / Manage All).
4. **P3 — Appearance model** (global + per-widget overrides) and UI scale.
5. **P4 — Settings window restructure** + Widget Manager.
6. **P5 — Hotkeys** (legacy action set, remapped).
7. **P6 — Startup optimisation** based on the P0 baseline.
8. **P7 — Distribution & AV hygiene** (added 2026-10-16): sensor-driver/antivirus mitigation —
   packer-like publish flags reduced *(pilot done)*, LibreHardwareMonitor upgrade **0.9.3 → 0.9.6**
   (WinRing0 → Microsoft-signed **PawnIO** backend, blocklist-safe), optional **compatibility mode
   (no sensor driver)**, no-admin driver strategy (kernel drivers inherently need admin), README
   guidance, FP reports, code signing when a certificate exists.

---

## 5. Decisions (locked 2026-10-16)

1. **Sidebar:** stays, but **optional** — floating widgets become the default presentation; the sidebar
   is an opt-in container and must not be a startup dependency.
2. **"On desktop (behind windows)" z-order mode:** implemented alongside Always-on-top / Normal.
3. **Legacy appearance settings** (alert colour/blink, show PC name, 24 h clock, date format):
   preserved and exposed as **global defaults and per-widget overrides**.
4. **UI Scale:** implemented **globally and per-widget**.
5. **Legacy hotkeys:** full set kept; `CycleEdge`/`CycleScreen` operate on the sidebar when it is
   enabled (assumption — revisit in P5).

P0 baseline is published ([STARTUP-BASELINE.md](STARTUP-BASELINE.md)); **P1 is implemented and
log-verified** (see the P1 notes above) and awaits interactive confirmation of the tray actions on
the user's desktop; P2 is next.

---

## 6. Distribution / AV findings (added 2026-10-16)

Bitdefender (the active AV on the dev machine) flagged and quarantined the sensor driver dropped by
LibreHardwareMonitor 0.9.3 as `SysDash.sys` (`Gen:Application.Venus.Winring.Cy17ay1@auVvKTci`,
sha256 `11bd2c9f…0ee5`; evidence in its quarantine pending-actions log). Findings:

- The driver is named after the host executable, is unsigned, and belongs to the WinRing0 family —
  a well-known AV false-positive class (HWiNFO, OpenHardwareMonitor, multiple vendor variants).
- The portable EXE previously used single-file **compression + full self-extraction** (packer-like
  signals). `scripts\build-release.ps1` now publishes plain single-file
  (`IncludeNativeLibrariesForSelfExtract` only) — verified working; the resulting exe is ~166 MB
  (the installer's own compression absorbs the size in distribution).
- All SysDash binaries are unsigned (no code-signing certificate on hand).
- Planned mitigations live in **P7**: compatibility mode (no sensor driver), LHM upgrade,
  README guidance for exclusions/FP reports, and code signing when a certificate is available.
- Note: Bitdefender has also been observed flagging this project's own PowerShell automation
  (`ATC.Malicious` / `SuspiciousBehavior.D28E183572768FA9`) — dev-machine exclusions
  (`D:\Projects\Personal Work\Sidebar Diagnostics`, `E:\Codex\temp\opencode`, `%TEMP%\.net\SysDash`,
  `%LOCALAPPDATA%\Programs\SysDash`) are recommended before build/test cycles.

**Field evidence — 2026-10-16, 08:05–11:12 (P1 verification builds):**

- Bitdefender quarantined the driver **twice** that day (both `Gen:Application.Venus.Winring.Cy17ay1@auVvKTci`):
  08:05 from the installed build (`%LOCALAPPDATA%\Programs\SysDash\SysDash.sys`) and 11:12 from the
  **portable** build (`artifacts\SysDash-win-x64\SysDash.sys`) — genuine interference, not only FP noise.
- Driver **drop location**: LHM 0.9.3 writes the .sys **next to the running exe**
  (`Path.ChangeExtension(MainModule.FileName, ".sys")`) → it lands in the distribution folder (portable)
  or the install folder (installed). Both ends up quarantined; AV noise follows the distribution folder.
- Driver **service** (`R0SysDash` — LHM derives `R0` + process name): installed twice at 11:11:41/11:11:43
  (SCM 7045) and **failed to start both times — "Access is denied"** (SCM 7000). No
  `Microsoft-Windows-CodeIntegrity/Operational` events in that window → Bitdefender is the prime suspect;
  ring0 sensors (CPU temp etc.) were dead in that session.
- **Windows context on the dev machine**: build 26300 (26H2), **HVCI (Memory Integrity) running**,
  `VulnerableDriverBlocklistEnable=1` → WinRing0 is blocklisted; the eventual fix must be
  blocklist-safe on such machines.
- **Admin nuance**: agent-shell launches run elevated (High IL), so the service install succeeds and only
  the *start* was denied. A normal user double-click cannot install a kernel driver service at all —
  kernel drivers inherently need admin. P7 must define the no-admin story: driverless default and/or a
  one-time signed-driver setup (PawnIO installer), with graceful sensor degradation otherwise.
- **Upgrade target confirmed**: LibreHardwareMonitor **0.9.6** (Feb 2026). 0.9.4+ replaces WinRing0 with
  **PawnIO** (namazso; Microsoft-signed kernel driver — blocklist-safe, low AV noise); 0.9.5 added an
  option to disable Ring0 driver installation; 0.9.6 ships PawnIO modules 2.2.
