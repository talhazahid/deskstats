# Store validation research — 2026-10-18

Decision: hold another release build. Exact cause of Partner Center's three inconclusive checks is not established. Do not represent a local test pass as Store approval.

## Findings and source quality

- Exact-match Microsoft Q&A, August 2024: https://learn.microsoft.com/en-us/answers/questions/1922205/how-do-i-solve-the-problem-that-the-silent-install
  Accepted answer recommends MSIX because of UAC. This conflicts with current normative EXE/MSI requirements explicitly allowing UAC. The thread does not demonstrate successful certification after conversion. Do not adopt that recommendation as a confirmed fix.
- Exact-match Microsoft Q&A, October 2026: https://learn.microsoft.com/en-us/answers/questions/6031821/package-validation-error-silent-install-check-add
  Reporter confirms exit 0 and populated metadata; only AI-generated advice, rejected by reporter. No proven fix.
- Developer investigation: https://github.com/TheUnboundDeveloper/Register-Broker/blob/main/CHANGELOG.md
  Reports invalid installer arguments and restricted PowerShell custom-action failure. Publisher changes alone did not solve results. Local reproductions are useful; no verified Store acceptance established. DeskStats uses Inno Pascal setup code, not that project's PowerShell configuration action, so copying its fix is unjustified.
- Older ARP/bundleware report: https://learn.microsoft.com/en-us/answers/questions/1330390/how-do-i-get-past-add-or-remove-program-and-bundle
  Dashboard support escalation recommended; no successful installer fix documented.

## Authoritative requirements

https://learn.microsoft.com/en-us/windows/apps/publish/publish-your-app/msi/app-package-requirements
- Trusted CA signing of installer and PE payload.
- Immutable versioned HTTPS direct installer URL.
- Offline standalone setup.
- No installer UI under supplied silent arguments; UAC is allowed.

https://learn.microsoft.com/en-us/windows/apps/publish/publish-your-app/msi/app-certification-process
- Test installation initiated from a standard-user account, with permitted elevation.
- Discoverable Start menu/Programs entry; populated product name, publisher, language and version.
- Clean uninstall; unrelated third-party software is prohibited; disclose driver/service dependencies.

https://learn.microsoft.com/en-us/windows/apps/publish/publish-your-app/msi/manual-package-validation
- Manual bundleware heuristic expects one new Programs and Features entry. Restoring automatic PawnIO adds a second dependency entry; record and disclose it, do not hide it or claim the one-entry test passes.

https://learn.microsoft.com/en-us/windows/msix/packaging-tool/know-your-installer
- MSIX does not support driver installation; conversion is not a drop-in solution for automatic PawnIO setup.

## DeskStats evidence and open gates

- Local 1.0.6 setup checked with Get-AuthenticodeSignature: NotSigned. Trusted signing is a confirmed unmet release requirement, not a proven cause of the question marks.
- Installer name DeskStats; screenshot dashboard name DeskStats: System Monitor. Confirm exact reserved/display name and publisher before normalizing identity; no proof this difference causes the results.
- Publisher currently DeskStats Project (GPLv3). Confirm actual publisher identity; do not invent a legal publisher.
- Inno switches already match supported syntax: /SP- /VERYSILENT /SUPPRESSMSGBOXES /NORESTART.
- Earlier CI proves completion and metadata, not absence of UI (it used WindowStyle Hidden).
- Previous deferred 1.0.5 clean install added exactly one app entry and still received inconclusive Partner Center results. PawnIO is not established as their cause.
- Setup uses admin privileges, bundled pinned PawnIO, skipifsilent app launch and synchronous prerequisite installation.

## Evidence required before next release

1. Confirm publisher identity and arrange trusted signing for installer and relevant payload; verify chain and timestamps.
2. Run exact final candidate on clean Windows with an interactive desktop without artificially hiding setup; record any installer/child windows, exit code and full logs. Check standard-user initiation with legitimate elevation, offline installation, fresh install, upgrade and uninstall.
3. Capture registry entries in both views, Start menu entry, dependency outcomes and preservation of user settings. Test missing/existing PawnIO and failure/reboot behavior without suppressing genuine errors.
4. Preserve versioned binary/hash and compare published download bytes to tested bytes.
5. Disclose required PawnIO and obtain Microsoft's driver-dependency assessment. Current policy page displays v7.20 effective October 22, 2026 (future relative to this report); verify applicable policy version for submission date.
6. If precheck remains inconclusive after those checks, obtain validator execution details through Partner Center support. Do not assert a backend outage or make another speculative packaging change.
