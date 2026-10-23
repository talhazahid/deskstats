# DeskStats macOS port handoff

## Status and scope

The repository contains the Windows implementation through release 1.0.14: seven application projects, the WPF integration checks, graphics, themes, installer scripts, MSIX packaging source, website and release history. It does not contain a macOS implementation. This document transfers the product context; a new coding agent does not automatically receive the conversation history.

The working tree was clean at the audit. No submodules are required. NuGet dependencies are restored from the PackageReference versions in project files. There is no committed dependency lock file, so transitive dependency resolution is not fully pinned. SDK selection is in global.json; all projects currently inherit net8.0-windows10.0.19041.0 from Directory.Build.props. Windows WPF projects cannot run natively on macOS.

## Start here

1. README.md: product overview and Windows build instructions; some statements are aspirational and should be checked against code.
2. CHANGELOG.md: current behavior and release limitations.
3. docs/ARCHITECTURE.md: project graph and widget/session contracts. It is an earlier target-architecture document, not a substitute for current source.
4. src/SysDash.App/App.xaml.cs: startup, dependency injection, metrics, layout and shutdown.
5. src/SysDash.App/LayoutController.cs, WidgetWindow.cs and SidebarWindow.xaml.cs: hosting, size, placement, docking and persistence.
6. src/SysDash.Core/Models/MetricsSnapshot.cs and src/SysDash.SDK/Session: data and session contracts.
7. src/SysDash.Widgets: views, settings and view models; src/SysDash.Theming: visual tokens and charts.
8. tests/SysDash.SetupChecks: current Windows behavior checks. docs/validation contains published test summaries, not the entire local test output.

## Source inventory

| Project | Responsibility | Porting considerations |
| --- | --- | --- |
| SysDash.Core | Reading models, history, settings infrastructure, diagnostics and utilities | Extract platform-neutral models first; the project also includes Windows-specific diagnostics. Do not assume the whole assembly is portable. |
| SysDash.SDK | Widget, host and session contracts | Contracts currently expose WPF types, including FrameworkElement. Separate UI-neutral contracts from platform views. |
| SysDash.Monitoring | LibreHardwareMonitor and CPU/memory/network/storage collection | Replace Windows collectors and performance counters with macOS implementations. Preserve null/unavailable semantics. |
| SysDash.Theming | Dark/light tokens, cards, rings, sparklines and Win32 window helpers | Recreate visuals in the chosen macOS UI; replace Win32 helpers. |
| SysDash.Layouts | JSON models, presets, atomic persistence and backups | Reuse schemas and algorithms where possible after removing Windows target dependencies. Map physical-display identities explicitly. |
| SysDash.Widgets | Hardware, time, internet and system widget implementations | Business logic may transfer; WPF views and OS integrations need replacement. |
| SysDash.App | WPF shell, tray, settings, autostart, update/install flows | Requires a macOS shell, status-menu integration, lifecycle and packaging. |

## Required behavior from the product owner

- Answer the owner in English even when requests are spoken in Urdu.
- Maintain consistent visual tokens, font hierarchy and spacing. Avoid introducing font weights 600 or 700; use normal/medium weights.
- First installation shows hardware widgets, including CPU, GPU, Memory and Sensors. Current Windows defaults also include Network and Storage.
- Users can choose which widgets are active, hidden or removed. Preserve those decisions; an intentionally empty saved layout stays empty.
- Floating widgets remain draggable. Saving placement must not lock them.
- Remember stable monitor identity and relative position, widget width, Regular/Large selection, theme and settings through tray exit and restart.
- Automatic display changes, monitor timeout and temporary disconnection must not overwrite the user's chosen monitor anchor. Return to the original display when available.
- Distinguish physical displays from macOS Spaces. Windows tests do not verify virtual-desktop membership; do not claim Spaces behavior is already implemented.
- Docking into the sidebar must move overlapping floating widgets clear, while leaving unaffected positions alone.
- Size is per widget: Regular or Large. Large is a proportional 1.35x content scale, not a literal 2x enlargement. Text, rings, charts and spacing scale together. Large floating widgets expand to content height without an internal scrollbar. A sidebar containing multiple widgets may require its own overall scrolling policy.
- In-place updates retain state. A fresh Windows reinstall restores defaults and archives the earlier layout. Confirm corresponding macOS install-generation semantics before copying the Windows marker approach.
- Updates are checked on app startup when enabled; download is checksum-verified, then the user confirms installation/restart. Source commits alone do not constitute releases.

## Important Windows implementation lessons

- WPF shuts windows before OnExit. Tray Exit must save/dispose the layout controller before Application.Shutdown, otherwise host close handlers can delete all widget entries. Apply equivalent ordering on macOS.
- A ScrollViewer intercepted bubbling mouse events and broke dragging. Preview handling now preserves buttons/scrollbar interaction. Test drag initiation alongside real controls.
- Store monitor-relative coordinates in logical units with explicit physical-pixel conversion; avoid saving OS-forced moves as intentional user changes.
- Layout.Clone must preserve instance IDs and every persisted field, including LargeSize and HasSavedState.
- User data lives under AppPaths.DataDirectory, normally LocalAppData/DeskStats on Windows. Use an appropriate macOS Application Support location and retain atomic writes/backups.
- Missing readings are null/unavailable, not zero. Do not fabricate temperatures, fan RPM or power readings.

## Platform replacements to investigate

The language/UI choice is not decided. A native AppKit/SwiftUI shell or a cross-platform .NET UI would both require real platform work. First establish the supported macOS versions, Apple Silicon/Intel scope and distribution channel with the owner.

- CPU/memory: evaluate documented Mach host statistics and sysctl interfaces.
- Network/storage: native interface counters and filesystem APIs; keep units and sampling intervals consistent.
- GPU, temperature, fan and power: investigate actual hardware/API availability separately for Intel and Apple Silicon. Do not promise feature parity or App Store eligibility before testing permissions and API support.
- Windowing: display identity, backing scale, global coordinate spaces, edge docking, menu bar/Dock work areas, screen changes and Spaces behavior.
- Packaging: app bundle, signing/notarization and updates appropriate to the chosen channel. Do not run the Windows EXE updater on macOS.

PawnIO is a Windows kernel driver and is not a macOS sensor backend. The Windows Store candidate's first-launch driver installer was rejected under policies 10.1.5 and 10.6.3 on October 23, 2026. Do not carry that installer flow into a Mac port or present the MSIX candidate as approved. Sensor privilege design is a separate engineering task.

## Included and intentionally excluded files

- Included: source, XAML, project files, solution, assets, notices, licensing, tests and build/release scripts.
- Not included: personal widget layouts, credentials, certificates/private keys, conversation history, bin/obj, restored NuGet packages, most generated test renders and local artifacts.
- installer/PawnIO_setup.exe is intentionally ignored; scripts/build-release.ps1 downloads official 2.2.0 and verifies its pinned SHA-256 and signature. The macOS port does not need this executable.
- The optional SDK tools downloaded into artifacts for local MSIX packaging are not committed. Windows packaging requires the documented SDK tools.
- GPLv3 license and third-party notices apply. Review LICENSE, NOTICE and docs/COMPLIANCE.md before distributing a port.

## Verification plan for the port

Start with CPU, memory, network and storage collectors plus one floating widget. Then add GPU/sensor capability discovery, sidebar, multi-monitor persistence, size options and update/distribution support. Keep the Windows implementation working while introducing platform boundaries.

Test fresh state, customized state, hidden and removed widgets, empty state, Regular/Large roundtrips, immediate exit before debounce, app restart, login startup, display sleep/wake without system sleep, system sleep/wake, disconnection/reconnection, mixed scales and Spaces. Exercise long labels, real populated sensor lists, small displays and content growth. Existing Windows checks are a reference; they are not macOS tests.

## Current limitations to carry forward

- Physical monitor timeout and virtual-desktop membership were not fully automated in Windows tests.
- Full live updater download/UAC/install/relaunch testing remains pending. A checksum from the same HTTPS release channel is integrity checking, not independent publisher authentication. Windows release binaries are unsigned.
- Sidebar relocation searches available space; its crowded-desktop fallback can require further work.
- MSIX startup, optional driver installation and sensor-access behavior require separate deployment testing and Store review.

## Suggested first instruction for the next coding session

Read docs/MACOS-HANDOFF.md, CHANGELOG.md and docs/ARCHITECTURE.md, then inspect the current implementation. Plan a native macOS port of DeskStats with equivalent widget behavior. Keep Windows source intact, separate platform-specific collectors and windowing, and do not assume PawnIO or WPF works on macOS. Validate sensor availability on actual target hardware before promising parity. Preserve user choices, sizes and display positions, and visually QA the result.

References:
- https://learn.microsoft.com/en-us/dotnet/desktop/wpf/overview/
- https://developer.apple.com/documentation/kernel/1502863-host_statistics64
