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

Version 1.0.3 is the full installer with PawnIO. Version 1.0.2 omitted PawnIO and is superseded. Never replace bytes behind an already-submitted versioned URL.

## Certification notes — required driver disclosure

DeskStats uses LibreHardwareMonitor 0.9.6 and requires the third-party PawnIO kernel driver for CPU temperature, hardware clock and power readings. Setup bundles the unmodified official PawnIO 2.2.0 installer, verifies its hash and publisher signature at build time, and invokes it silently with `-install -silent` only when the service and library are missing. DeskStats setup requires administrator privileges.

Official dependency: https://github.com/namazso/PawnIO.Setup/releases/tag/2.2.0

Dependency installer SHA-256: `1F519A22E47187F70A1379A48CA604981C4FCF694F4E65B734AAA74A9FBA3032`

The dependency installer is signed by namazso.eu. The installed driver signature is recorded in the verification report; a Microsoft Windows Hardware Compatibility Publisher signature is evidence for review, rather than a guarantee of policy approval or proof of every WHCP requirement.

On a clean PC, installation creates DeskStats and PawnIO entries in Programs and Features. PawnIO is an explicitly disclosed functional dependency, not an unrelated optional application. We request case-by-case review of the dependency under policy 10.2.4. No registry entries are concealed or renamed to alter validation results. Uninstalling DeskStats preserves the shared PawnIO installation and existing user settings.

## Store description prerequisite disclosure

DeskStats installs the PawnIO sensor driver and requires administrator permissions for full CPU hardware sensor readings.

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
