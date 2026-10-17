# GPL Compliance & Attribution Notes

## 1. License posture of the new product

**SysDash is GPLv3.** Rationale:

- The original SidebarDiagnostics project is GPLv3.
- SysDash reuses *functional* pieces of the original (sensor-selection
  heuristics, unit converters, appbar/monitor techniques, perf-counter
  sampling). Under the GPL, adaptaion of those parts makes the resulting work a
  derivative ("a work based on the Program"), so 17 U.S.C. § 101 / GPL § 5
  requires the combined work to be licensed under the GPL.
- Choosing GPLv3 for the whole codebase is the safe and correct call: it keeps
  the product free, matches the upstream lineage, and lets us reuse upstream
  code without a second opinion from legal.

Consequences we accept and document:
- Anyone distributing SysDash (or modified versions) must provide the
  Corresponding Source under GPLv3.
- The original SidebarDiagnostics repository must remain linked / credited
  (NOTICE + README do this).
- We do NOT dual-license or add a proprietary CLA; contributions are GPLv3.

## 2. What is actually derivative

| SysDash file | Derives from | How |
|---|---|---|
| `SysDash.Monitoring/SensorCatalog.cs` | `Monitoring.cs` `InitCPU/InitRAM/InitGPU` | adapted sensor-selection logic |
| `SysDash.Monitoring/DriveSampler.cs` | `Monitoring.cs` `DriveMonitor` | perf-counter counters + fallbacks |
| `SysDash.Monitoring/NetworkSampler.cs` | `Monitoring.cs` `NetworkMonitor` | adapter delta/IP patterns |
| `SysDash.Monitoring/Converters.cs` | `Monitoring.cs` converters | ported unit math |
| `SysDash.Theming/MonitorGeometry.cs` | `Windows.cs` `Monitor` | DPI + monitor enumeration |

Those files carry header comments pointing to the original file and this
document. The UI, widget contract, layout engine, and app shell are a fresh
design and are also GPLv3 by policy.

## 3. Third-party components and their licenses

- **LibreHardwareMonitor** — MPL-2.0, dynamically linked NuGet package.
  MPL-2.0 allows combination with GPL code; MPL-covered files (we do not modify
  them) stay under the MPL. If we patch the lib, the modified files must be
  provided under MPL-2.0.
- **Hardcodet.NotifyIcon.Wpf** — CPOL; permissive, permits reuse in GPL apps.
- **Microsoft.Extensions.DependencyInjection** — MIT.
- **System.Diagnostics.PerformanceCounter** — MIT.
- **Open-Meteo** — free API; no key required; data licensed under CC-BY 4.0;
  attribution is displayed in the weather widget tooltip/about.
- **Symbols/graphics** — original `sidebar.ico` NOT copied; SysDash uses new
  inline SVG geometries authored for this project.

## 4. Attribution checklist (implemented)

- [x] `NOTICE` at repo root listing upstream + per-file derivative notes
- [x] `docs/THIRD_PARTY/SidebarDiagnostics-LICENSE.md` (upstream GPL text)
- [x] `docs/THIRD_PARTY/` directory reserved for other dependency licenses
- [x] README credits SidebarDiagnostics and LibreHardwareMonitor
- [x] Header comments on derivative files point to the original source
- [x] About/credits entry planned in the settings UI (Phase 6)

## 5. What you may NOT do

- Re-license SysDash or subparts under a proprietary license.
- Distribute SysDash without source (GPL § 6).
- Remove the copyright notices from derivative files.

## 6. If you want a permissive widget ecosystem (future)

A widget that **only links against SysDash.SDK** and contains **no copied code**
can still be distributed under the GPL (the SDK itself is GPL). If we ever want
third-party widgets under permissive terms, the SDK would need to be
independently authored clean-room or the project re-licensed by all copyright
holders — both are deliberately out of scope for the first release.