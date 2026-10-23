<#
.SYNOPSIS
    Builds the Microsoft Store (MSIX) package of DeskStats.

.DESCRIPTION
    The Store build runs as a normal user (an MSIX cannot request administrator rights or
    install a driver), so sensors that need the PawnIO driver (CPU temperature / clock / power)
    are not available in it. Everything else works. The EXE installer from build-release.ps1
    remains the full-featured build.

    Get the three identity values from Partner Center → your product → Product management →
    Product identity: "Package/Identity/Name", "Package/Identity/Publisher" and
    "Package/Properties/PublisherDisplayName".

    Needs: .NET 8 SDK and the Windows 10/11 SDK (provides makeappx.exe).
    Output: artifacts\DeskStats-<Version>.msix   (upload this file in Partner Center)

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File scripts\build-msix.ps1 `
        -IdentityName "12345NXTLABS.DeskStats" `
        -Publisher "CN=XXXXXXXX-XXXX-XXXX-XXXX-XXXXXXXXXXXX" `
        -PublisherDisplayName "NXTLABS"
#>
[CmdletBinding()]
param(
    [string]$IdentityName = 'Geeklabs.DeskStatsSystemMonitor',
    [string]$Publisher = 'CN=5B135F6F-4181-4A69-A7BE-6BAA39528EBE',
    [string]$PublisherDisplayName = 'Owl Dev',
    [string]$Version = '1.0.0.0',          # 4 parts; the Store requires the last part to be 0
    [string]$Arch = 'win-x64',
    [string]$PfxPath,                      # optional: sign for local testing only (the Store re-signs)
    [string]$PfxPassword
)

$ErrorActionPreference = 'Stop'
$Root = Split-Path -Parent $PSScriptRoot
$AppProj = Join-Path $Root 'src\SysDash.App\SysDash.App.csproj'
$Artifacts = Join-Path $Root 'artifacts'
$Stage = Join-Path $Artifacts 'msix-stage'
$Publish = Join-Path $Artifacts 'DeskStats-store-win-x64'
$Output = Join-Path $Artifacts "DeskStats-$Version.msix"
$buildId = [Guid]::NewGuid().ToString('N')
$Stage = Join-Path $Artifacts ('msix-stage-' + $buildId)
$Publish = Join-Path $Artifacts ('msix-publish-' + $buildId)

if ($Version -notmatch '^\d+\.\d+\.\d+\.0$') { throw "Version must look like 1.0.0.0 (last part 0). Got '$Version'." }

# ---- tools
$dotnet = Get-Command dotnet -ErrorAction SilentlyContinue
if (-not $dotnet) { throw 'The .NET 8 SDK is required: https://dotnet.microsoft.com/download/dotnet/8.0' }
$kits = Join-Path ${env:ProgramFiles(x86)} 'Windows Kits\10\bin'
$makeappx = Get-ChildItem -Path $kits -Recurse -Filter makeappx.exe -ErrorAction SilentlyContinue |
    Where-Object { $_.FullName -match '\\x64\\' } | Sort-Object FullName -Descending | Select-Object -First 1
if (-not $makeappx) { $makeappx = Get-Item -LiteralPath (Join-Path $Artifacts 'windows-sdk-tools\bin\10.0.26100.0\x64\makeappx.exe') -ErrorAction SilentlyContinue }
if (-not $makeappx) { throw 'makeappx.exe not found. Install the Windows 10/11 SDK.' }

# ---- publish the Store variant (asInvoker manifest, no elevation)
& $dotnet.Source publish $AppProj -c Release -r $Arch --self-contained true `
    -p:Version=$Version -p:StoreBuild=true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:DebugType=None -p:DebugSymbols=false -p:SatelliteResourceLanguages=en -o $Publish
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed (exit $LASTEXITCODE)." }
if (-not (Test-Path (Join-Path $Publish 'DeskStats.exe'))) { throw 'DeskStats.exe was not produced.' }

# ---- stage: app files + assets + manifest
New-Item -ItemType Directory -Path $Stage | Out-Null
Copy-Item (Join-Path $Publish '*') $Stage -Recurse -Force
Copy-Item (Join-Path $Root 'packaging\msix\Assets') (Join-Path $Stage 'Assets') -Recurse -Force
Get-ChildItem -LiteralPath (Join-Path $Stage 'Assets') -Filter '*.scale-100.png' | ForEach-Object {
    Copy-Item -LiteralPath $_.FullName -Destination (Join-Path $_.DirectoryName ($_.Name.Replace('.scale-100', '')))
}

$driver = Join-Path $Root 'installer\PawnIO_setup.exe'
if ((Get-FileHash -LiteralPath $driver).Hash -ne '1F519A22E47187F70A1379A48CA604981C4FCF694F4E65B734AAA74A9FBA3032') { throw 'PawnIO hash mismatch.' }
if ((Get-AuthenticodeSignature -LiteralPath $driver).Status -ne 'Valid') { throw 'PawnIO signature invalid.' }
New-Item -ItemType Directory -Path (Join-Path $Stage 'Dependencies') | Out-Null
Copy-Item -LiteralPath $driver -Destination (Join-Path $Stage 'Dependencies\PawnIO_setup.exe')
Copy-Item -LiteralPath (Join-Path $Root 'LICENSE'),(Join-Path $Root 'NOTICE') -Destination $Stage
$IdentityName = [Security.SecurityElement]::Escape($IdentityName)
$Publisher = [Security.SecurityElement]::Escape($Publisher)
$PublisherDisplayName = [Security.SecurityElement]::Escape($PublisherDisplayName)

$manifest = Get-Content (Join-Path $Root 'packaging\msix\AppxManifest.template.xml') -Raw
$manifest = $manifest.Replace('{{IdentityName}}', $IdentityName).Replace('{{Publisher}}', $Publisher).
    Replace('{{PublisherDisplayName}}', $PublisherDisplayName).Replace('{{Version}}', $Version)
Set-Content -Path (Join-Path $Stage 'AppxManifest.xml') -Value $manifest -Encoding UTF8

# ---- pack
if (Test-Path $Output) { Remove-Item $Output -Force }
& $makeappx.FullName pack /d $Stage /p $Output /o
if ($LASTEXITCODE -ne 0) { throw "makeappx failed (exit $LASTEXITCODE)." }

if ($PfxPath) {
    $signtool = Get-ChildItem -Path $kits -Recurse -Filter signtool.exe | Where-Object { $_.FullName -match '\\x64\\' } |
        Sort-Object FullName -Descending | Select-Object -First 1
    & $signtool.FullName sign /fd SHA256 /f $PfxPath /p $PfxPassword $Output
    if ($LASTEXITCODE -ne 0) { throw "signtool failed (exit $LASTEXITCODE)." }
}

$mb = [math]::Round((Get-Item $Output).Length / 1MB, 1)
Write-Host "Built $Output ($mb MB)" -ForegroundColor Green
Write-Host 'Upload this .msix in Partner Center → Packages (the Store signs it for you).'
