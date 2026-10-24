# DeskStats for macOS

Desktop hardware monitor widgets for macOS, ported from the Windows app
[DeskStats](https://github.com/talhazahid/deskstats) by Talha Zahid.

This is a native-feeling Electron app that recreates the upstream widget set,
themes, layout model, settings dialogs and tray menu on Apple Silicon. It is
not a virtual machine or a Wine wrapper: every view is rebuilt in HTML/CSS from
the original XAML and reads real data through macOS system tools and
purpose-built helpers.

```
CPU · GPU · Memory · Network · Storage · Date & Time · World Clock
Weather · Sensors · Now Playing · Trash
```

## Requirements

- **Apple Silicon Mac** (M1 or newer). The bundled sensor helper (`macmon`) is
  an arm64 binary. Intel support is planned but not included yet.
- macOS 11 Big Sur or newer.
- **No Xcode and no Xcode licence acceptance are required** to build or run
  this app. The `.app` is assembled with `@electron/packager` and signed ad hoc
  with the system `codesign`.
- Node.js 18+ only if you build from source.

## Run the packaged app

```
dist/DeskStats.app
```

Unzip `DeskStats-<version>-arm64.zip` and **drag DeskStats.app into
Applications** before opening it. Running it straight from Documents, Desktop
or Downloads makes macOS ask for folder access first, and startup waits on that
prompt. The build is unsigned, so
the first launch needs one of:

- right-click the app → **Open** → **Open**; or
- **System Settings → Privacy & Security → Open Anyway**.

The app runs as a menu-bar utility (`LSUIElement`), so it does not appear in the
Dock. Look for the DeskStats icon in the menu bar; every widget can be opened
from there.

## Run from source

```sh
npm install
npm start
```

Build a fresh Apple Silicon bundle and zip:

```sh
npm run pack
# → dist/DeskStats-darwin-arm64/DeskStats.app
# → dist/DeskStats-1.0.14-arm64.zip
```

## What is included

**Widgets** — CPU (load graph, per-core rings, clock, power, temperature),
GPU (load, VRAM, temperature, clock), Memory (used/total, swap, top processes),
Network (throughput graph, adapter picker, public IP, session totals), Storage
(per-volume ring, read/write rates and graph), Date & Time (big clock, date),
World Clock (multiple zones), Weather (current conditions, hourly and daily
forecast, air quality), Sensors (temperature rings, fan ring, sensor list),
Now Playing (artwork, title/artist/album, progress, transport buttons) and
Trash (item count and size, empty action).

**Layout modes** — floating always-on-top widgets or a docked sidebar stack.
The sidebar supports 1–3 responsive columns, Regular/Large sizing (+35 %),
resizable width, auto-hide with a hover hot zone, pinning, and an optional
background tint/vibrancy.

**Themes and appearance** — per-widget dark/light theme overrides, opacity,
card frame on/off, accent colour, layer ("always on top" / normal), click-through
with a rescue pill, snap-to-grid dragging, per-monitor placement and offsets.

**Layouts** — the upstream presets plus custom layouts. Layouts, settings and
per-widget state are stored as JSON under:

```
~/Library/Application Support/DeskStats
```

**Tray menu** — add/remove/show widgets, switch widget/sidebar mode, choose a
layout, save the current layout, switch theme, reset positions, open the
hardware report, app settings, About, and quit.

## macOS details

- **Sensors**: `macmon` supplies CPU/GPU power, clocks, per-core load,
  temperatures and memory without `sudo`. Missing readings show `—` instead of
  invented numbers.
- **Voltages**: Apple Silicon has no public voltage sensor, so DeskStats reads
  the SoC power manager's DVFS tables (`ioreg` → `pmgr` → `voltage-states*`)
  and looks up the voltage for the live CPU cluster / GPU frequency. **Core
  voltage** is the highest rail any CPU cluster is requesting; the hardware
  report lists each cluster. The GPU widget shows the GPU rail voltage.
- **Memory clock**: macOS reports only the memory type (LPDDR4X/5/5X), so the
  GPU widget shows the chip's rated unified-memory speed in MT/s (for example
  M3 = LPDDR5-6400), plus live memory power from `macmon`.
- **Fanless Macs**: when the helper reports no fans (MacBook Air and other
  passively cooled models), the CPU/GPU details say **No fan** and the Sensors
  widget shows a **No fan / FANLESS** ring. No fake 0 RPM reading.
- **Now Playing**: uses the bundled `nowplaying-cli` against the system media
  session.
- **Weather**: Open-Meteo forecast, air-quality and geocoding APIs, called
  directly with no API key. Attribution: weather data by
  [Open-Meteo.com](https://open-meteo.com/) (CC BY 4.0).
- **Launch at login**: the upstream “Start with Windows” setting maps to
  macOS Login Items.
- **Trash**: replaces the Recycle Bin widget; “Empty” clears the user’s Trash.

## Known limitations

- **Apple Silicon only** in this build.
- **“On desktop” layer**: macOS has no public equivalent of the Windows
  desktop layer. Widgets pinned to the desktop behave like normal-level
  windows (they stay behind always-on-top windows but can still be covered).
- **Ctrl-hover click-through rescue**: macOS does not expose global modifier
  state to the app without extra permissions, so the Ctrl-hover interaction is
  not implemented. Click-through widgets instead show a small rescue pill after
  hovering, and the tray menu can always restore interactivity.
- **Sensor coverage** is smaller than on Windows: macOS does not expose every
  voltage, per-sensor temperature or fan reading.
- **Updates**: the app checks the upstream GitHub releases feed. Because this
  is a macOS port, a newer upstream Windows release is reported only in the log
  (automatic checks are silent); “Check for updates…” in App settings shows the
  result.

## Project layout

```
src/main/       Electron main process: windows, trays, metrics, helpers, IPC
src/renderer/   Widgets, sidebar, dialogs and shared styling
resources/      macmon binary + licence, nowplaying-cli bundle
build/          DeskStats.icns and packaging assets
scripts/pack.js Apple Silicon packaging + ad-hoc signing + zip
```

## Licence and credits

DeskStats for macOS is free software under the **GNU GPL v3** (see
[LICENSE](LICENSE)). It is a derivative work of the GPLv3 DeskStats / SysDash
project and, through it, of SidebarDiagnostics.

- Original Windows app: [talhazahid/deskstats](https://github.com/talhazahid/deskstats)
- Sensor helper: [macmon](https://github.com/vladkens/macmon) (MIT)
- Media helper: [nowplaying-cli](https://github.com/kirtan-shah/nowplaying-cli) (GPLv3)
- Runtime: [Electron](https://www.electronjs.org) (MIT)
- Weather: [Open-Meteo](https://open-meteo.com/) (CC BY 4.0)

See [NOTICE](NOTICE) for the complete third-party attribution.
