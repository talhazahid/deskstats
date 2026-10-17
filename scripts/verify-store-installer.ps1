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
    [string]$ExpectedPublisher = 'DeskStats Project (GPLv3)'
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
$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$log = Join-Path $output "$stamp-install.log"
$report = Join-Path $output "$stamp-result.json"
$process = Start-Process -FilePath $installerPath -WindowStyle Hidden -PassThru -ArgumentList @(
    '/SP-', '/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', ('/LOG="' + $log + '"')
)
if (-not $process.WaitForExit(600000)) {
    throw "Setup is still running after ten minutes. Inspect it and $log before retrying."
}
$process.Refresh()
$entry = Get-ItemProperty -LiteralPath 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\{3F0A2E9C-7B1D-4C5A-8F6E-2D9B0A1C4E5F}_is1' -ErrorAction SilentlyContinue
$appPath = Join-Path $env:ProgramFiles 'DeskStats\DeskStats.exe'
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
}
$passed = $result.InstallSucceeded -and $result.AppNameMatches -and $result.PublisherMatches -and $result.VersionMatches -and $result.AppPresent -and $result.UninstallCommandPresent -and $result.SilentUninstallCommandPresent
$result['Passed'] = $passed
$result | ConvertTo-Json | Set-Content -LiteralPath $report -Encoding UTF8
Write-Host "Verification report: $report"
if (-not $passed) { throw "Installer verification failed. See $report and $log. Do not submit this build yet." }
Write-Host 'Installation and installed-app metadata checks passed. Also verify no installer or bundled-driver UI appeared on a clean test PC.'
