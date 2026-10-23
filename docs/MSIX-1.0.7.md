# DeskStats MSIX 1.0.7.0

Identity: Geeklabs.DeskStatsSystemMonitor
Publisher: CN=5B135F6F-4181-4A69-A7BE-6BAA39528EBE
Publisher display name: Owl Dev

The widgets load before the optional sensor setup window. When PawnIO is missing, the user can install the bundled official 2.2.0 driver or continue with limited readings. The choice is remembered; installation remains available from the tray. Installation verifies the pinned SHA-256 and uses Windows elevation only for the requested installer. No driver is installed during MSIX deployment. Cancellation, errors and reboot requests remain visible in the dialog.

This package declares runFullTrust and allowElevation; restricted capabilities and the driver dependency require Store assessment. This is a technical candidate, not a claim of Store approval. Normal-user driver access may still limit hardware readings after installation; no driver device permissions are weakened.

QA: dotnet publish and MakeAppx manifest validation/packing passed. Verified packaged identity, capability declarations, bundled driver hash. WPF checks passed for Continue in dark/light themes, missing/tampered installer rejection and widget positioning/persistence. Dialog render inspected. Installation and first-launch driver execution from a deployed MSIX have NOT been tested; this remains required before claiming end-to-end success.

artifacts/DeskStats-1.0.7.0.msix is unsigned for Partner Center upload. It is not a ready-to-double-click local installer. Microsoft signs accepted Store packages; local deployment testing requires a separately signed test copy and appropriate test-certificate trust.
