@echo off
setlocal
cd /d "%~dp0"

echo === Stopping any running SysDash ===
taskkill /f /im SysDash.exe >nul 2>&1

echo === Building SysDash (Release) - please wait ===
dotnet build SysDash.sln -c Release > build-log.txt 2>&1
if errorlevel 1 (
    echo.
    echo BUILD FAILED. Errors:
    findstr /i /c:"error" build-log.txt
    echo.
    echo Full log saved to: %~dp0build-log.txt
    pause
    exit /b 1
)

echo === Build OK. Starting SysDash ===
start "" "%~dp0src\SysDash.App\bin\Release\net8.0-windows10.0.19041.0\SysDash.exe"
echo Done. SysDash is running in the tray.
timeout /t 3 >nul
