@echo off
cd /d "%~dp0"

:: ── Self-elevation (Run as Admin) ──────────────────────────────────────────
>nul 2>&1 "%SYSTEMROOT%\system32\cacls.exe" "%SYSTEMROOT%\system32\config\system"
if '%errorlevel%' NEQ '0' (
    echo Requesting administrative privileges...
    goto UACPrompt
) else ( goto gotAdmin )

:UACPrompt
    echo Set UAC = CreateObject^("Shell.Application"^) > "%temp%\getadmin.vbs"
    echo UAC.ShellExecute "%~s0", "", "", "runas", 1 >> "%temp%\getadmin.vbs"
    "%temp%\getadmin.vbs"
    exit /B

:gotAdmin
    if exist "%temp%\getadmin.vbs" ( del "%temp%\getadmin.vbs" )
    pushd "%cd%"
    CD /D "%~dp0"
:: ────────────────────────────────────────────────────────────────────────────

echo ========================================
echo System Maintenance Build and Run Script
echo ========================================
echo.

echo [1/5] Stopping services and cleaning up processes...
net stop WinMaintenance 2>nul
net stop RansomGuardSentinel 2>nul
taskkill /IM MaintenanceUI.exe /F 2>nul
taskkill /IM MaintenanceWorker.exe /F 2>nul
taskkill /IM RansomGuard.exe /F 2>nul
taskkill /IM RansomGuard.Watchdog.exe /F 2>nul
timeout /t 2 /nobreak >nul

echo [+] Clearing historical activity data...
del /q "C:\ProgramData\RansomGuard\activity_log.*" 2>nul
del /q "C:\ProgramData\RansomGuard\Logs\*.log" 2>nul
echo Done.

echo [2/5] Building projects...
dotnet build RansomGuard.csproj -v q
if %errorlevel% neq 0 (
    echo Build failed on first attempt, retrying...
    dotnet build RansomGuard.csproj -v q
)

echo [3/5] Publishing service with all dependencies...
dotnet publish RansomGuard.Service/RansomGuard.Service.csproj -c Debug -o RansomGuard.Service/publish --self-contained true -r win-x64 -v q

echo [4/5] Starting service...
net start WinMaintenance
if %errorlevel% neq 0 (
    echo WARNING: Service failed to start. Check Event Viewer for details.
    pause
    exit /b 1
)

echo [5/5] Launching Maintenance UI...
timeout /t 3 /nobreak >nul
cd /d "%~dp0"
start "" "%~dp0bin\Debug\net8.0-windows\MaintenanceUI.exe"

echo.
echo ========================================
echo STEALTH MODE ACTIVE! 
echo ========================================
echo Maintenance Service is running.
echo UI will appear in the system tray shortly.
echo.
pause
