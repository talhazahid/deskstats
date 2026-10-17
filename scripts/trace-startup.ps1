<#
.SYNOPSIS
    Runs SysDash and prints the [startup] trace marks from its log.

.DESCRIPTION
    Measurement helper for startup work (see docs\STARTUP-BASELINE.md). Moves the
    current log aside so each run is captured cleanly, launches the given EXE,
    waits, prints all [startup] lines, then force-stops the app.

    NOTE: force-stops any running SysDash first (kill by process name).

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File scripts\trace-startup.ps1
    powershell -ExecutionPolicy Bypass -File scripts\trace-startup.ps1 -Exe artifacts\SysDash-win-x64\SysDash.exe -Cold
#>
[CmdletBinding()]
param(
    [string]$Exe = "$PSScriptRoot\..\artifacts\SysDash-win-x64\SysDash.exe",
    [int]$WaitSeconds = 20,
    [switch]$Cold
)

$ErrorActionPreference = 'Stop'

$logDir = Join-Path $env:LOCALAPPDATA 'SysDash\logs'
$log = Join-Path $logDir ("app-{0}.log" -f (Get-Date -Format yyyyMMdd))

Get-Process SysDash -ErrorAction SilentlyContinue | Stop-Process -Force
while (Get-Process SysDash -ErrorAction SilentlyContinue) { Start-Sleep -Milliseconds 100 }

if ($Cold) {
    # Clears the single-file extraction cache (simulates first run after an update).
    Remove-Item -Recurse -Force (Join-Path $env:TEMP '.net\SysDash') -ErrorAction SilentlyContinue
}

if (Test-Path $log) {
    Move-Item $log ($log + '.prev') -Force
}

$before = Get-Date
$p = Start-Process -FilePath $Exe -PassThru
Write-Output ("launched pid {0} at {1:HH:mm:ss.fff}" -f $p.Id, $before)
Start-Sleep -Seconds $WaitSeconds

if ($p.HasExited) {
    Write-Output ("PROCESS EXITED EARLY (code {0})" -f $p.ExitCode)
} else {
    Write-Output ("process alive after {0}s - OK" -f $WaitSeconds)
}

if (Test-Path $log) {
    Get-Content $log | Where-Object {
        $_ -match '\[startup\]' -or $_ -match 'SysDash started' -or
        $_ -match 'LibreHardwareMonitor ready' -or $_ -match 'MetricsService started'
    }
} else {
    Write-Output 'LOG FILE NOT FOUND'
}

if (-not $p.HasExited) {
    Stop-Process -Id $p.Id -Force
}
