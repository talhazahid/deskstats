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

Version 1.0.6 restores automatic PawnIO installation during setup in both the website and Store-named EXE. Windows requests administrator approval when setup needs elevation. There is no separate first-launch driver prompt. Existing versioned downloads remain unchanged. The Store-named file is an EXE submission candidate, not an approved Store package.

Version 1.0.2 omitted the dependency entirely and is superseded. Never replace bytes behind an already-submitted versioned URL.

## Certification notes — required driver disclosure

DeskStats uses LibreHardwareMonitor 0.9.6 and requires the third-party PawnIO kernel driver for CPU temperature, hardware clock and power readings. Both editions bundle the unmodified official PawnIO 2.2.0 installer and verify its hash and publisher signature at build time. Both editions install the driver during setup using `-install -silent`, skipping installation when the required service and library already exist. DeskStats setup requires administrator privileges.

Official dependency: https://github.com/namazso/PawnIO.Setup/releases/tag/2.2.0

Dependency installer SHA-256: `1F519A22E47187F70A1379A48CA604981C4FCF694F4E65B734AAA74A9FBA3032`

The dependency installer is signed by namazso.eu. The installed driver signature is recorded in the verification report; a Microsoft Windows Hardware Compatibility Publisher signature is evidence for review, rather than a guarantee of policy approval or proof of every WHCP requirement.

During a fresh 1.0.6 installation, both DeskStats and its required PawnIO dependency have visible Programs and Features entries. Setup verifies dependency installation before modifying app files and reports installation or reboot errors. The bundled installer remains available for an explicit repair request from the app.

PawnIO is an explicitly disclosed functional dependency, not an unrelated optional application. We request case-by-case review of this driver dependency under policy 10.2.4. Automatic dependency installation does not establish Store compliance. No registry entries are concealed or renamed to alter validation results. Uninstalling DeskStats preserves the shared PawnIO installation and existing user settings.

## Store description prerequisite disclosure

Full CPU temperature, hardware clock and power readings require the PawnIO sensor driver and administrator permissions. DeskStats setup installs this required dependency automatically, with Windows administrator approval when needed.

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
