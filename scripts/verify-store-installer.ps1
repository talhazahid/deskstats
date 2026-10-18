<#
.SYNOPSIS
Installs a real DeskStats setup silently and verifies its Windows uninstall entry.
.DESCRIPTION
Run in an elevated PowerShell on a test PC. This installs or upgrades DeskStats.
Writes the setup log and JSON evidence to artifacts/store-validation.
It does not prove Microsoft Store certification or the absence of visible UI.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$Installer,
    [Parameter(Mandatory = $true)][string]$ExpectedVersion,
    [string]$ExpectedPublisher = 'DeskStats Project (GPLv3)',
    [switch]$RequireFreshInstall,
    [switch]$DeferredDriver
)
$ErrorActionPreference = 'Stop'
$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
$principal = New-Object Security.Principal.WindowsPrincipal($identity)
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw 'Open PowerShell as administrator before running installer verification.'
}
$installerPath = (Resolve-Path -LiteralPath $Installer).Path
$output = Join-Path (Split-Path -Parent $PSScriptRoot) 'artifacts\store-validation'
New-Item -ItemType Directory -Path $output -Force | Out-Null
$stamp = Get-Date -Format 'yyyyMMdd-HHmmss-fff'
$log = Join-Path $output "$stamp-install.log"
$report = Join-Path $output "$stamp-result.json"
function Get-VisibleInstalledApps {
    $roots = @(
        'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\*',
        'HKLM:\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\*',
        'HKCU:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\*',
        'HKCU:\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\*'
    )
    @(Get-ItemProperty -Path $roots -ErrorAction SilentlyContinue |
        Where-Object { $_.DisplayName -and $_.SystemComponent -ne 1 } |
        Select-Object PSPath, DisplayName, Publisher, DisplayVersion)
}
$before = @(Get-VisibleInstalledApps)
$beforeKeys = @($before | ForEach-Object { $_.PSPath })
if ($RequireFreshInstall -and ($before.DisplayName -contains 'DeskStats' -or
    (Test-Path 'HKLM:\SYSTEM\CurrentControlSet\Services\PawnIO'))) {
    throw 'Fresh-install verification requires a clean test PC without DeskStats or PawnIO.'
}
$process = Start-Process -FilePath $installerPath -WindowStyle Hidden -PassThru -ArgumentList @(
    '/SP-', '/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', ('/LOG="' + $log + '"')
)
if (-not $process.WaitForExit(600000)) {
    throw "Setup is still running after ten minutes. Inspect it and $log before retrying."
}
$process.Refresh()
$after = @(Get-VisibleInstalledApps)
$newEntries = @($after | Where-Object { $_.PSPath -notin $beforeKeys })
$appKeySuffix = '\{3F0A2E9C-7B1D-4C5A-8F6E-2D9B0A1C4E5F}_is1'
$extraEntries = @($newEntries | Where-Object { -not $_.PSPath.EndsWith($appKeySuffix) })
$unexpectedEntries = @($extraEntries | Where-Object {
    $_.DisplayName -ne 'PawnIO' -or $_.Publisher -ne 'namazso' -or
    -not $_.PSPath.EndsWith('\PawnIO')
})
$deskStatsEntries = @($after | Where-Object { $_.DisplayName -eq 'DeskStats' })
$entry = Get-ItemProperty -LiteralPath 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\{3F0A2E9C-7B1D-4C5A-8F6E-2D9B0A1C4E5F}_is1' -ErrorAction SilentlyContinue
$appPath = Join-Path $env:ProgramFiles 'DeskStats\DeskStats.exe'
$driver = Get-ItemProperty 'HKLM:\SYSTEM\CurrentControlSet\Services\PawnIO' -ErrorAction SilentlyContinue
$driverPath = if ($driver.ImagePath) { [Environment]::ExpandEnvironmentVariables($driver.ImagePath.Trim('"').Replace('\SystemRoot', $env:windir)) } else { '' }
$driverSignature = if ($driverPath -and (Test-Path -LiteralPath $driverPath)) { Get-AuthenticodeSignature -LiteralPath $driverPath } else { $null }
$installerSignature = Get-AuthenticodeSignature -LiteralPath $installerPath
$result = [ordered]@{
    Installer = $installerPath
    SHA256 = (Get-FileHash -LiteralPath $installerPath -Algorithm SHA256).Hash
    ExitCode = $process.ExitCode
    InstallSucceeded = ($process.ExitCode -eq 0)
    AppName = $entry.DisplayName
    AppNameMatches = ($entry.DisplayName -eq 'DeskStats')
    Publisher = $entry.Publisher
    PublisherMatches = ($entry.Publisher -eq $ExpectedPublisher)
    Version = $entry.DisplayVersion
    VersionMatches = ($entry.DisplayVersion -eq $ExpectedVersion)
    AppPresent = (Test-Path -LiteralPath $appPath)
    UninstallCommandPresent = (-not [string]::IsNullOrWhiteSpace($entry.UninstallString))
    SilentUninstallCommandPresent = (-not [string]::IsNullOrWhiteSpace($entry.QuietUninstallString))
    Log = $log
    NewInstalledApps = @($newEntries)
    NoExtraInstalledApps = ($extraEntries.Count -eq 0)
    ExactlyOneDeskStatsEntry = ($deskStatsEntries.Count -eq 1)
    NoUnexpectedInstalledApps = ($unexpectedEntries.Count -eq 0)
    RequiredDriverRegistered = ($null -ne $driver)
    RequiredDriverLibraryPresent = (Test-Path (Join-Path $env:ProgramFiles 'PawnIO\PawnIOLib.dll'))
    DriverPath = $driverPath
    DriverSignatureValid = ($null -ne $driverSignature -and $driverSignature.Status -eq 'Valid')
    DriverSigner = $driverSignature.SignerCertificate.Subject
    InstallerSignatureValid = ($installerSignature.Status -eq 'Valid')
    MicrosoftDriverDependencyReviewRequired = $true
    StoreCertificationReady = $false
    DeferredDriver = [bool]$DeferredDriver
    BundledInstallerVerified = ((Test-Path (Join-Path $env:ProgramFiles 'DeskStats\Dependencies\PawnIO_setup.exe')) -and
        (Get-FileHash (Join-Path $env:ProgramFiles 'DeskStats\Dependencies\PawnIO_setup.exe')).Hash -eq '1F519A22E47187F70A1379A48CA604981C4FCF694F4E65B734AAA74A9FBA3032')
}
$dependencyPassed = $result.RequiredDriverRegistered -and $result.RequiredDriverLibraryPresent -and $result.DriverSignatureValid
if ($DeferredDriver) {
    $dependencyPassed = $result.NoExtraInstalledApps -and -not $result.RequiredDriverRegistered -and -not $result.RequiredDriverLibraryPresent
}
$passed = $result.InstallSucceeded -and $result.AppNameMatches -and $result.PublisherMatches -and $result.VersionMatches -and $result.AppPresent -and $result.UninstallCommandPresent -and $result.SilentUninstallCommandPresent -and $result.NoUnexpectedInstalledApps -and $result.ExactlyOneDeskStatsEntry -and $result.BundledInstallerVerified -and $dependencyPassed
$result['Passed'] = $passed
$result | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $report -Encoding UTF8
Write-Host "Verification report: $report"
if (-not $passed) { throw "Installer verification failed. See $report and $log. Do not submit this build yet." }
Write-Host 'Functional installer checks passed, including the required signed PawnIO driver. This does not certify Store compliance. Review signature status and disclose the driver dependency to Microsoft; confirm absence of installer UI on an interactive test PC.'
