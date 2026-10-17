; DeskStats installer — Inno Setup 6 script.
; Build driven by scripts/build-release.ps1 (defines MyAppVersion / SourceDir / OutputDir).
;
; Machine-wide install (like SidebarDiagnostics): Program Files\DeskStats, one UAC prompt.
; The PawnIO sensor driver (CPU temperature / clock / power) is bundled and
; installed silently when it is not already present — no extra step for the user.
; User data (settings/layouts/logs) lives in %LOCALAPPDATA%\DeskStats and is NOT
; removed on uninstall. PawnIO is shared with other tools and is NOT removed either.

#ifndef MyAppVersion
  #define MyAppVersion "1.0.0"
#endif
#ifndef SourceDir
  #define SourceDir "..\artifacts\DeskStats-win-x64"
#endif
#ifndef OutputDir
  #define OutputDir "..\artifacts"
#endif

#define MyAppName "DeskStats"
#define MyAppExeName "DeskStats.exe"
#define MyAppPublisher "DeskStats Project (GPLv3)"

; The PawnIO setup must sit next to this script (build-release.ps1 downloads it).
#define PawnSetup AddBackslash(SourcePath) + "PawnIO_setup.exe"
#if !FileExists(PawnSetup)
  #error PawnIO_setup.exe is missing next to installer.iss - run scripts\build-release.ps1 (it downloads it).
#endif

[Setup]
AppId={{3F0A2E9C-7B1D-4C5A-8F6E-2D9B0A1C4E5F}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={autopf}\DeskStats
UsePreviousAppDir=no
DefaultGroupName=DeskStats
DisableProgramGroupPage=yes
PrivilegesRequired=admin
OutputDir={#OutputDir}
OutputBaseFilename=DeskStats-Setup-{#MyAppVersion}
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
UninstallDisplayName={#MyAppName} {#MyAppVersion}
UninstallDisplayIcon={app}\{#MyAppExeName}
AppMutex=DeskStats_SingleInstance_Mutex
CloseApplications=no

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a &desktop shortcut"; GroupDescription: "Additional icons:"

[Files]
; App binaries (self-contained single-file EXE plus any side files the publish produced)
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
; License + compliance docs (GPLv3 requires shipping the license text)
Source: "..\LICENSE";                         DestDir: "{app}\docs"; Flags: ignoreversion
Source: "..\NOTICE";                          DestDir: "{app}\docs"; Flags: ignoreversion
Source: "..\README.md";                       DestDir: "{app}\docs"; Flags: ignoreversion
Source: "..\docs\COMPLIANCE.md";              DestDir: "{app}\docs"; Flags: ignoreversion
; Sensor driver — always bundled, extracted only when it is not installed yet.
Source: "PawnIO_setup.exe"; DestDir: "{tmp}"; Flags: deleteafterinstall; Check: not PawnIOInstalled
Source: "..\docs\THIRD_PARTY\*";              DestDir: "{app}\docs\third_party"; Flags: ignoreversion recursesubdirs

[Icons]
Name: "{group}\DeskStats"; Filename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\DeskStats"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
; Setup is already elevated, so PawnIO installs silently with no second UAC prompt.
Filename: "{tmp}\PawnIO_setup.exe"; Parameters: "-install -silent"; StatusMsg: "Installing the sensor driver (CPU temperature)..."; Flags: runhidden waituntilterminated; Check: not PawnIOInstalled
; DeskStats runs as administrator; setup is elevated, so the app starts without another prompt.
Filename: "{app}\{#MyAppExeName}"; Description: "Launch {#MyAppName} now"; Flags: nowait postinstall skipifsilent shellexec

[UninstallDelete]
; Only remove app files+shortcuts. NEVER delete %LOCALAPPDATA%\DeskStats: it holds the user's widgets,
; layouts and settings, which come back automatically after a reinstall or an update.

[Code]
function PawnIOInstalled(): Boolean;
begin
  Result := RegKeyExists(HKLM, 'SYSTEM\CurrentControlSet\Services\PawnIO');
end;

// Best-effort: stop a running DeskStats (or old SysDash) before install/upgrade so file replaces don't fail.
function InitializeSetup(): Boolean;
var
  ResultCode: Integer;
begin
  Result := True;
  Exec('taskkill.exe', '/IM DeskStats.exe /T /F', '', 0, ewWaitUntilTerminated, ResultCode);
  Exec('taskkill.exe', '/IM SysDash.exe /T /F', '', 0, ewWaitUntilTerminated, ResultCode);
end;