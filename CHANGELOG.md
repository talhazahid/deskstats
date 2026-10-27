# Changelog

## 1.0.19 — 2026-10-27

- Corrected the release packaging: silent updates now use the committed installer that honors /UPDATEAPP=1 and relaunches DeskStats after installation.
- Production builds require a clean source checkout to prevent local installer overrides from removing the update relaunch step.
- Added installer logs alongside downloaded updates so installation and relaunch failures can be diagnosed.
- Includes the native Windows tray-tooltip fix from 1.0.18.

## 1.0.18 — 2026-10-27

- Fixed the stray DeskStats label that could appear at the primary screen's top-left without clicking the tray icon.
- Updated the tray library to 2.0.1 so plain hover text uses the native Windows tooltip instead of a WPF popup.
- Verified repeated tooltip-open callbacks, text changes, layout persistence, widget sizing, power readings and update prompt behavior.

## 1.0.17 — 2026-10-24

- Moved Hardware Power into an independent hardware widget with its own position, Regular/Large size and saved settings. Fresh default layouts include it; existing layouts can add it from Hardware > Hardware power.
- Added read-only AMD driver queries for separately supported total board and ASIC/chip power. No scaling, TDP substitution or guessed total.
- Label unsupported board power explicitly and identify each GPU by name. Clear old watts when readings disappear and show the sample time.
- Stop treating an arbitrary CPU power sensor as CPU package power.
- QA: power scope/unsupported/invalid/zero/stale-data checks, layout persistence, Large sizing and standalone registration passed. Live RX 6600 probe returned ASIC power only (34–40 W across 12 low-load samples); this interface did not expose total board power. A gaming-load comparison against Radeon metrics remains unverified.

## 1.0.16 — 2026-10-24

- Show update checking, download progress and verification in a dedicated update window.
- Bring the ready-to-install prompt to the front with a taskbar entry.
- Explicitly say Install update and restart DeskStats so the prompt cannot be mistaken for a system restart.
- Start automatic checks shortly after launch and show download or installer errors.
- Verified update-window visibility, Later behavior, existing layout persistence and sensor checks.

## 1.0.15 — 2026-10-24

- Added Hardware Power to Hardware Sensors, showing separate CPU, GPU and other available wattage sensors.
- Enabled PSU discovery for telemetry-capable hardware supported by LibreHardwareMonitor.
- Kept sensor scopes separate: package/core/rail readings are never summed into a fake whole-PC total. PSU output is not labeled as wall-socket consumption.
- Clarified headings: CPU Details, CPU Threads, GPU Details, Memory (RAM), Process Memory, Hardware Sensors, Hardware Temperatures and Fan Speeds.
- Added power tests for unavailable readings, valid zero values, invalid values, package fallback deduplication and PSU labeling. Existing layout and UI checks passed; Regular widget renders inspected.
- Actual PSU telemetry has not been tested against a supported physical PSU. Availability depends on hardware.

## 1.0.14 — 2026-10-23

### Added
- Startup update checks and verified installer downloads for the EXE edition.
- An Updates section in app settings and a tray action to check manually.
- A versioned update manifest and a script to publish its URL and SHA-256.
- Fresh-install detection: reinstalling restores the hardware defaults and archives the previous layout; upgrading keeps the customized layout.

### Fixes included since 1.0.5
- Floating widget dragging through scroll containers, while preserving interactive controls.
- Docking moves overlapping floating widgets clear of the sidebar.
- Tray exit saves widget state before WPF closes windows.
- Persisted widget visibility, removals, monitor anchors, positions and Regular/Large size.
- Preserved monitor anchors through automatic display relocation.
- Intentionally empty layouts remain empty on restart.
- A tray action restores all hardware widgets without duplicating existing instances.
- Per-widget Regular/Large sizing; Large increases content scale by 35% and expands vertically without an internal scrollbar.

### Packaging and validation
- Includes the MSIX packaging source and assets used for the earlier Store candidate. The October 23 Store review rejected its driver-install flow and allowElevation request; this release does not claim Store approval.
- EXE setup installs the bundled official PawnIO dependency when missing.
- Build and WPF checks cover fresh/reinstall/upgrade state, docking, placement, size persistence and update manifest validation. Settings UI was rendered and inspected.
- Physical monitor timeout and the full download/UAC/install/relaunch update cycle still require live testing. Installer and app remain unsigned.

The update service consumes published installers and release metadata; pushing source changes alone does not distribute an update. Installation requests confirmation and Windows administrator approval when required.
