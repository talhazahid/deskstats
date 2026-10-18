# DeskStats EXE submission

## Package fields

- Architecture: x64
- App type: EXE
- Installer switches: `/SP- /VERYSILENT /SUPPRESSMSGBOXES /NORESTART`
- Requires silent switches: yes; leave the "does not require switches" box unchecked.
- Language: English
- Success: 0
- Canceled before installation: 2
- Other standard mappings: leave blank unless the precise scenario matches the documented meaning.
- Return-code documentation: https://talhazahid.github.io/deskstats/installer-return-codes.html

Version 1.0.5 provides two distinct installers. `DeskStats-Setup-1.0.5.exe` is the website edition and installs PawnIO during setup. `DeskStats-Store-Setup-1.0.5.exe` is an EXE submission candidate that keeps the verified driver installer locally but waits for an explicit first-launch choice before installing it. The app also offers "Continue with limited readings" and remembers that choice. This candidate is not an MSIX package and is not claimed to be approved for Store distribution.

Version 1.0.2 omitted the dependency entirely and is superseded. Never replace bytes behind an already-submitted versioned URL.

## Certification notes — required driver disclosure

DeskStats uses LibreHardwareMonitor 0.9.6 and requires the third-party PawnIO kernel driver for CPU temperature, hardware clock and power readings. Both editions bundle the unmodified official PawnIO 2.2.0 installer and verify its hash and publisher signature at build time. The website edition installs the driver during setup. The deferred candidate waits for an explicit request in the running app before invoking the driver installer with `-install -silent`. DeskStats setup requires administrator privileges.

Official dependency: https://github.com/namazso/PawnIO.Setup/releases/tag/2.2.0

Dependency installer SHA-256: `1F519A22E47187F70A1379A48CA604981C4FCF694F4E65B734AAA74A9FBA3032`

The dependency installer is signed by namazso.eu. The installed driver signature is recorded in the verification report; a Microsoft Windows Hardware Compatibility Publisher signature is evidence for review, rather than a guarantee of policy approval or proof of every WHCP requirement.

For the deferred 1.0.5 candidate, initial setup creates only DeskStats in Programs and Features. On first launch, the user can choose "Install sensor driver" or "Continue with limited readings." Choosing installation verifies the bundled installer against its pinned SHA-256 hash, requests administrator approval, and installs PawnIO silently. A successful installation restarts DeskStats to refresh sensors; a driver restart request instead asks the user to restart Windows. Cancellation and installation errors allow retrying or continuing with limited readings. PawnIO then has its own visible entry in Programs and Features.

PawnIO is an explicitly disclosed functional dependency, not an unrelated optional application. We request case-by-case review of this driver dependency and its post-launch installation under policies 10.1.5 and 10.2.4. Moving installation to first launch does not establish compliance with those policies. No registry entries are concealed or renamed to alter validation results. Uninstalling DeskStats preserves the shared PawnIO installation and existing user settings.

## Store description prerequisite disclosure

Full CPU temperature, hardware clock and power readings require the PawnIO sensor driver and administrator permissions. DeskStats offers driver installation after launch, or you can continue with limited readings.

Place this disclosure at the beginning of the Store description.

## Readiness and remaining work

Functional installer tests and Store certification are separate. A functional pass does not imply that Microsoft accepted the dependency or that package prechecks will show green indicators.

Before submitting for certification:

1. Review the published fresh-install, reinstall and uninstall reports.
2. Sign DeskStats application binaries and the outer EXE installer with a trusted publisher code-signing certificate. The current automated builds are unsigned and are not Store-certification-ready.
3. Publish the signed result at a new immutable versioned HTTPS URL; rerun installer verification against those exact bytes.
4. Include the dependency disclosure and obtain Microsoft's case-by-case driver assessment. Do not assert that certification is guaranteed.
5. Verify silent operation on an interactive clean PC. The automated runner verifies completion and installed metadata, not every possible visible window or physical CPU sensor reading.

Microsoft references:

- https://learn.microsoft.com/en-us/windows/apps/publish/store-policies#102-security
- https://learn.microsoft.com/en-us/windows/apps/publish/publish-your-app/msi/manual-package-validation
- https://learn.microsoft.com/en-us/windows/apps/publish/publish-your-app/msi/app-package-requirements
