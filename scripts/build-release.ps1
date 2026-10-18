<#
.SYNOPSIS
    Builds DeskStats and produces a distributable EXE.

.DESCRIPTION
    Publishes SysDash.App as a self-contained, single-file EXE for Windows
    (default: win-x64) and — if Inno Setup is available — wraps it in a
    double-clickable setup installer EXE.

    Outputs:
      artifacts\DeskStats-Setup-x.y.z.exe   installer EXE (recommended; installs the
                                             PawnIO sensor driver automatically)
      artifacts\DeskStats-win-x64\DeskStats.exe   portable EXE (testing only)

.EXAMPLE
    # Everything from scratch, auto-installing the .NET 8 SDK if missing
    powershell -ExecutionPolicy Bypass -File scripts\build-release.ps1 -InstallSdk

.EXAMPLE
    # Quick rebuild, no SDK install, no Inno Setup
    powershell -ExecutionPolicy Bypass -File scripts\build-release.ps1 -SkipInstaller
#>
[CmdletBinding()]
param(
    [string]$Version = '1.0.0',
    [string]$Arch = 'win-x64',
    [switch]$InstallSdk,
    [switch]$SkipInstaller,
    [switch]$DeferredDriver
)

$ErrorActionPreference = 'Stop'
$Root = Split-Path -Parent $PSScriptRoot
$AppProj = Join-Path $Root 'src\SysDash.App\SysDash.App.csproj'
$Artifacts = Join-Path $Root 'artifacts'
$PublishDir = Join-Path $Artifacts "DeskStats-$Arch"
$SetupOut = Join-Path $Artifacts "DeskStats-Setup-$Version.exe"
if ($DeferredDriver) { $SetupOut = Join-Path $Artifacts "DeskStats-Store-Setup-$Version.exe" }

# ---------------------------------------------------------------- .NET SDK
$dotnet = Get-Command dotnet -ErrorAction SilentlyContinue
if (-not $dotnet) {
    if ($InstallSdk) {
        Write-Host 'dotnet SDK not found — installing via winget...' -ForegroundColor Yellow
        if (-not (Get-Command winget -ErrorAction SilentlyContinue)) {
            throw 'winget is not available either. Install the .NET 8 SDK from https://dotnet.microsoft.com/download/dotnet/8.0 and re-run.'
        }
        winget install --id Microsoft.DotNet.SDK.8 --silent --accept-package-agreements --accept-source-agreements
        # winget installs to Program Files; refresh PATH for this process
        $env:PATH = "$env:PATH;$env:ProgramFiles\dotnet"
        $dotnet = Get-Command dotnet -ErrorAction SilentlyContinue
        if (-not $dotnet) { throw 'dotnet still not found after install. Open a new terminal and re-run.' }
    } else {
        throw "The .NET 8 SDK is required. Either run with -InstallSdk (uses winget) or install it from https://dotnet.microsoft.com/download/dotnet/8.0"
    }
}

# ---------------------------------------------------------------- Version
$Version = $Version.TrimStart('v')
Write-Host "Building DeskStats $Version for $Arch" -ForegroundColor Cyan

# ---------------------------------------------------------------- Publish
$out = $null; $err = $null
& $dotnet.Source publish $AppProj `
    -c Release `
    -r $Arch `
    --self-contained true `
    -p:Version=$Version `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:DebugType=None -p:DebugSymbols=false `
    -p:SatelliteResourceLanguages=en `
    -o $PublishDir 2>&1 | Tee-Object -Variable out
if ($LASTEXITCODE -ne 0) {
    Write-Host ($out -join "`n") -ForegroundColor Red
    throw "dotnet publish failed (exit $LASTEXITCODE). Fix the reported errors and re-run."
}

$exe = Join-Path $PublishDir 'DeskStats.exe'
if (-not (Test-Path $exe)) { throw "Expected $exe was not produced." }
$sizeMB = [math]::Round((Get-Item $exe).Length / 1MB, 1)
Write-Host "Published portable EXE: $exe ($sizeMB MB)" -ForegroundColor Green

if ($SkipInstaller) {
    Write-Host "Skipped installer (used -SkipInstaller). Run $exe directly or zip the folder." -ForegroundColor Yellow
    exit 0
}

# ---------------------------------------------------------------- PawnIO (sensor driver)
# LibreHardwareMonitor 0.9.5+ needs the PawnIO driver for CPU temperature /
# clocks / power. The installer bundles PawnIO_setup.exe and installs it
# silently, so the user never has to do anything. This step is REQUIRED.
$PawnSetup = Join-Path $Root 'installer\PawnIO_setup.exe'
$PawnHash = '1F519A22E47187F70A1379A48CA604981C4FCF694F4E65B734AAA74A9FBA3032'
if (-not (Test-Path $PawnSetup)) {
    [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
    Invoke-WebRequest -UseBasicParsing -Uri 'https://github.com/namazso/PawnIO.Setup/releases/download/2.2.0/PawnIO_setup.exe' -OutFile $PawnSetup
}
if ((Get-FileHash -LiteralPath $PawnSetup -Algorithm SHA256).Hash -ne $PawnHash) {
    throw 'PawnIO setup does not match the pinned official 2.2.0 release. Build stopped.'
}
$PawnSignature = Get-AuthenticodeSignature -LiteralPath $PawnSetup
if ($PawnSignature.Status -ne 'Valid' -or $PawnSignature.SignerCertificate.Subject -notmatch 'CN=namazso\.eu(?:,|$)') {
    throw 'PawnIO setup publisher signature is invalid or unexpected. Build stopped.'
}
Write-Host "Verified PawnIO 2.2.0: $PawnSetup" -ForegroundColor Green

# ---------------------------------------------------------------- Installer
# Locate Inno Setup 6 (iscc.exe); install it with winget when missing.
function Find-Iscc {
    $cmd = Get-Command iscc -ErrorAction SilentlyContinue
    if ($cmd) { return $cmd.Source }
    foreach ($c in @(
        "$env:ProgramFiles\Inno Setup 6\ISCC.exe",
        "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
        "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe")) {
        if (Test-Path $c) { return $c }
    }
    return $null
}

$isccPath = Find-Iscc
if (-not $isccPath) {
    Write-Host 'Inno Setup 6 not found - installing via winget...' -ForegroundColor Yellow
    if (Get-Command winget -ErrorAction SilentlyContinue) {
        winget install --id JRSoftware.InnoSetup --exact --silent --accept-package-agreements --accept-source-agreements
        $isccPath = Find-Iscc
    }
    if (-not $isccPath) {
        throw 'Inno Setup 6 is required for the setup EXE. Install it from https://jrsoftware.org/isdl.php and re-run.'
    }
}

Write-Host "Building installer with Inno Setup: $isccPath" -ForegroundColor Cyan
$iss = Join-Path $Root 'installer\installer.iss'
& $isccPath $iss `
    "/DMyAppVersion=$Version" `
    "/DDeferredDriver=$([int][bool]$DeferredDriver)" `
    "/DSourceDir=$PublishDir" `
    "/DOutputDir=$Artifacts" 2>&1 | Tee-Object -Variable out
if ($LASTEXITCODE -ne 0) {
    Write-Host ($out -join "`n") -ForegroundColor Red
    exit $LASTEXITCODE
}

if ($DeferredDriver) {
    Move-Item -LiteralPath (Join-Path $Artifacts "DeskStats-Setup-$Version.exe") -Destination $SetupOut -Force
}
if (-not (Test-Path $SetupOut)) { throw "Expected installer $SetupOut was not produced." }
Write-Host "Installer: $SetupOut" -ForegroundColor Green
Write-Host 'Done.' -ForegroundColor Green
