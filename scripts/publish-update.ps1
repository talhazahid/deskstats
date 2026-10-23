[CmdletBinding()]
param([Parameter(Mandatory)][ValidatePattern('^\d+\.\d+\.\d+$')][string]$Version)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$installer = Join-Path $root "docs/downloads/DeskStats-Setup-$Version.exe"
if (-not (Test-Path -LiteralPath $installer)) { throw 'Publish the versioned installer into docs/downloads first.' }
$feed = [ordered]@{
    version = $Version
    url = "https://talhazahid.github.io/deskstats/downloads/DeskStats-Setup-$Version.exe"
    sha256 = (Get-FileHash -LiteralPath $installer -Algorithm SHA256).Hash
}
$destination = Join-Path $root 'docs/updates.json'
if (Test-Path -LiteralPath $destination) {
    $previous = Get-Content -LiteralPath $destination -Raw | ConvertFrom-Json
    if ([version]$Version -lt [version]$previous.version) { throw 'Cannot publish an older update.' }
    if ($Version -eq $previous.version -and $feed.sha256 -ne $previous.sha256) { throw 'Do not replace a published version. Increment the version.' }
}
$feed | ConvertTo-Json | Set-Content -LiteralPath $destination -Encoding utf8
Write-Host "Updated release manifest for $Version. Commit and publish the installer and manifest together."
