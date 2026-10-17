# SysDash — Startup Baseline (P0)

Date: 2026-10-16 · Status: **complete** (P0 done — this is the input for P6)
Method: instrumentation only (`SysDash.Core.Diagnostics.StartupTrace` + `AppStartup` module
initializer). Every launch logs `[startup]` marks — absolute time, PID, stopwatch total/delta,
process→managed boot time — to `%LocalAppData%\SysDash\logs\app-<date>.log`. No behaviour changed.

Machine: Ryzen 5 5600X · RX 6600 · .NET Desktop Runtime 8.0.31 present · Defender real-time
protection **off** (so AV scanning is *not* a factor in these numbers).
Config: poll 1000 ms, GPU/drives/network on, dark theme, layout 'Gaming' (0 widgets) unless stated.
Re-measure any time with `scripts\trace-startup.ps1` (moves the previous log aside per run).

---

## 1. Measured scenarios (all times from process creation)

| # | Build | Scenario | Widgets | boot* | → visible sidebar | → first data |
|---|---|---|---|---|---|---|
| 0 | dev (framework-dep.) | very first run after build | 0 | ~6.05 s | *not reached (killed)* | — |
| A | dev | first full run after build | 0 | 3.37 s | *not reached (killed at 15 s)* | — |
| B | dev | warm | 0 | 0.12 s | 3.12 s | 4.00 s |
| S1 | **ship (single-file)** | **first-ever run of the bundle** | 0 | **14.12 s** | **~24.4 s** | ~25.2 s |
| S2 | ship | warm | 0 | 0.22 s | 2.10 s | 3.09 s |
| S4 | ship | cache cleared (simulates first run after update) | 0 | 3.43 s | 12.84 s | 13.65 s |
| W1 | ship | warm | 5 | 0.15 s | 1.92 s | 2.67 s |
| W2 | ship | warm | 5 | 0.15 s | 1.87 s | 2.59 s |

\* boot = process creation → managed module initializer (host + CLR + single-file extraction).

## 2. Phase breakdown

### Warm (ship, S2 / W1 / W2) — steady state users see every launch

| Phase | Time |
|---|---|
| boot (process → managed) | 0.15–0.22 s |
| App.xaml parse → OnStartup | 0.16–0.19 s |
| settings + DI container + layouts + theme applied | ~0.13 s total |
| **LHM hardware init (UI thread, inside MetricsService ctor)** | **0.69–0.78 s** |
| session + timer start | ~0.01 s |
| sidebar construction | 0.01–0.04 s |
| LayoutController resolve + apply + materialize | 0.07–0.09 s |
| tray installed | 0.05–0.07 s |
| `sidebar.Show()` (window creation + backdrop) | 0.31–0.36 s |
| first render after Show | 0.01 s |
| **→ window visible** | **1.9–2.1 s** |
| first snapshot built (first drive/network pass) | +0.7–1.0 s |
| **→ first data on screen** | **2.6–3.1 s** |

### Cold (ship, S1 / S4)

| Phase | S1 (first ever) | S4 (cache cleared) |
|---|---|---|
| **boot / bundle extraction** | **14.12 s** | 3.43 s |
| App.xaml → OnStartup | 2.08 s | 1.98 s |
| first-touch type load/JIT before Bootstrap | 3.06 s | 1.73 s |
| LHM hardware init | 1.45 s | 1.71 s |
| LayoutController resolve + apply | 1.63 s | 1.89 s |
| tray + Show + render (rest) | 0.67 s | 0.64 s |
| **→ window visible** | **~24.4 s** | **12.8 s** |

### Widgets (W1/W2, 5 docked widgets)

Materialization is cheap: `clock` 41–43 ms (first, includes type load), then `cpu` 13–16,
`ram` 12–14, `network` 9–11, `ram` 9–11 → **~86–94 ms for all five**. Five widgets did not move
the visible-window time vs the empty layout (1.87–1.92 s vs 2.10 s warm — within noise).
**"Loads slowly" is not a widget-count problem.**

---

## 3. Findings

1. **Warm launch ≈ 1.9–2.1 s to visible, 2.6–3.1 s to data.** Every launch pays for: LHM init
   (0.7–0.8 s, on the UI thread, before any UI exists), window creation/backdrop (0.3–0.4 s),
   boot (0.15–0.2 s) and ~0.3 s of misc startup work. The first snapshot adds ~0.85 s before
   any values appear.
2. **Cold launch is 9–24 s.** Dominated by single-file bundle extraction (3.4–14.1 s) + cold
   JIT/type-load gaps (1.7–6 s in two clusters) + LHM (1.5–1.7 s). Users hit this after every
   install/update, and any time `%TEMP%` is cleaned.
3. **Nothing paints until almost everything is done.** Current order: metrics start → LHM init →
   layout materialize → tray → `Show()`. The window could exist ~1.7 s earlier (warm) with the
   same data latency.
4. **The tray icon appears last** (after LHM + materialize), ~1.4–1.5 s warm, ~9–10 s cold.
5. Widget materialization, theme apply, DI, layout parsing are all 10–90 ms: not worth touching.
6. The dev-build "layout resolve 1396 ms warm" outlier (run B) did not reproduce in the ship
   builds (68–81 ms) — treat as environment noise from a mixed cold/warm sequence.

---

## 4. Implications for P6 (evidence-based candidates, not yet applied)

| Candidate | Removes (warm) | Removes (cold) | Risk |
|---|---|---|---|
| **A. Start LHM off the UI thread & after first paint** (open hardware on a background task; sampler waits for it) | ~0.7–0.8 s from visible path | ~1.5–1.7 s | Low — widgets already render "no data yet" states for the first second today |
| **B. Show the shell before materialize/metrics** (create + `Show()` sidebar, then fill) | ~0.1–0.2 s + overlaps everything after | similar | Low |
| **C. Build tray immediately after theme** (make its menu lazy so it doesn't need layout state at construction) | ~0.1 s, tray visible ~1.2 s earlier | several s earlier | Low |
| **D. Cut single-file costs**: try `IncludeAllContentForSelfExtract=false` (extraction only then native libs), `EnableCompressionInSingleFile=false`, and `PublishReadyToRun=true` | boot 0.15→~0.1 s | **3–14 s** | Medium — needs verification that WPF loads from bundle; R2R increases size |
| **E. Defer the first full snapshot** (CPU/RAM/GPU first, drives/network one tick later) | data ~0.3–0.4 s earlier | similar | Low, but changes data cadence — decide with user |

Expected result after A–C: warm → visible ≈ **0.5–0.8 s**, data ≈ 2.2–2.6 s (unchanged cadence);
tray ≈ 0.3 s. Cold → visible ≈ 1.5–2 s + boot; with D the boot penalty itself shrinks.

## 5. Caveats

- Single machine; the value is in the *shape* (which phase dominates), not exact milliseconds.
- Weather was never in the active layout during measurement; its first fetch is async by design
  (dispatcher timer, `_ = RefreshAsync()`), and must be re-confirmed with widgets present in P1/P2.
- The 5-widget scenario used the built-in `Default` layout (clock, cpu, ram, network, ram+details);
  the user's active `Gaming` layout (0 items) was restored afterwards.
- `scripts\trace-startup.ps1` force-stops any running SysDash before measuring.
- The instrumentation stays in the codebase: one `[startup]` block per launch in the log.

---

## 6. Post-change check — release flags (2026-10-16, P7 pilot)

`scripts\build-release.ps1` now publishes without `EnableCompressionInSingleFile` and
`IncludeAllContentForSelfExtract` (packer-like signals — see UI-REFERENCE-AUDIT.md §6). Verified on
the same machine and layout as the S-runs (ship single-file, 'Gaming', 0 widgets):

| Scenario | Old flags (S1/S2/S4) | New flags |
|---|---|---|
| warm → visible | 2.10 s | 2.02 s |
| warm → first data | 3.09 s | 2.82 s |
| cache cleared → visible | 12.84 s | **2.21 s** |
| cache cleared → first data | 13.65 s | **2.94 s** |
| first-ever run of the bundle | 24.4 s | ≈3.5 s visible / ≈9.0 s data¹ |

¹ That run paid a one-time AV scan of the brand-new 166 MB unsigned exe (LHM init 2.4 s in it vs
0.86 s on the next run). Not a bundle cost — the second run was already at 2.02 s.

Notes:

- The portable exe grows 73.5 MB → 166 MB (nothing is compressed anymore; the installer's own
  compression absorbs most of that in distribution, and P7 can revisit R2R/trimming later).
- Only native libraries are extracted now, so the `%TEMP%\.net\SysDash` extraction cache holds a
  few MB instead of ~150 MB; cold boots are dominated by the host start (~0.4 s), not extraction.
- Warm numbers are unchanged — the win is entirely in cold/update runs: **~13 s → ~2.2 s**.
