# Changelog

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
