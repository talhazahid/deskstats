[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$Installer,
    [Parameter(Mandatory)][string]$PayloadDirectory,
    [string]$ReportPath = 'artifacts/store-signature-preflight.json'
)
$ErrorActionPreference = 'Stop'
$installerFile = Get-Item -LiteralPath $Installer
$payload = Get-Item -LiteralPath $PayloadDirectory
if (-not $payload.PSIsContainer) { throw 'PayloadDirectory must be the final staged application directory.' }
$files = @($installerFile) + @(Get-ChildItem -LiteralPath $payload.FullName -Recurse -File | Where-Object { $_.Extension -in '.exe', '.dll', '.sys' })
$checks = @($files | ForEach-Object {
    $signature = Get-AuthenticodeSignature -LiteralPath $_.FullName
    [pscustomobject]@{
        File = $_.FullName
        SHA256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash
        Status = [string]$signature.Status
        Signer = $signature.SignerCertificate.Subject
        TimestampPresent = ($null -ne $signature.TimeStamperCertificate)
    }
})
$failed = @($checks | Where-Object Status -ne 'Valid')
$report = [ordered]@{
    CheckedAt = (Get-Date).ToUniversalTime().ToString('o')
    SignatureChecksPassed = ($failed.Count -eq 0)
    StoreCertificationReady = $false
    Scope = 'Installer and loose EXE/DLL/SYS files. Embedded single-file payload and generated uninstaller require separate verification; local chain trust does not prove Microsoft Trusted Root Program eligibility.'
    Files = $checks
}
$destination = [IO.Path]::GetFullPath($ReportPath)
New-Item -ItemType Directory -Force -Path (Split-Path $destination) | Out-Null
$report | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $destination -Encoding UTF8
if ($failed.Count) { throw "$($failed.Count) file(s) do not have valid Authenticode signatures. See $destination. Do not submit this candidate." }
Write-Host "Signature checks passed within the documented scope. Report: $destination"
