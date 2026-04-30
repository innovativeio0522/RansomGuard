@echo off
:: Start RansomGuard with LAN Discovery Enabled
:: Run this as Administrator

echo === Starting RansomGuard with LAN Discovery ===
echo.

:: Check for admin privileges
net session >nul 2>&1
if %errorLevel% neq 0 (
    echo ERROR: This script requires administrator privileges!
    echo.
    echo Please right-click this file and select "Run as Administrator"
    echo.
    pause
    exit /b 1
)

echo [1/5] Configuring firewall...
netsh advfirewall firewall delete rule name="RansomGuard LAN Discovery" >nul 2>&1
netsh advfirewall firewall add rule name="RansomGuard LAN Discovery" dir=in action=allow protocol=UDP localport=47700 profile=private,domain >nul 2>&1
netsh advfirewall firewall add rule name="RansomGuard LAN Discovery (Outbound)" dir=out action=allow protocol=UDP localport=47700 profile=private,domain >nul 2>&1
echo    Done!

echo.
echo [2/5] Building service...
cd RansomGuard.Service
dotnet publish -c Debug -r win-x64 --self-contained -o publish >nul 2>&1
cd ..
echo    Done!

echo.
echo [3/5] Installing and starting service...
sc stop RGService >nul 2>&1
sc delete RGService >nul 2>&1
timeout /t 2 /nobreak >nul

sc create RGService binPath= "%CD%\RansomGuard.Service\publish\RGService.exe" start= auto DisplayName= "RansomGuard Sentinel" >nul 2>&1
sc description RGService "Provides real-time ransomware protection and threat detection" >nul 2>&1
sc start RGService >nul 2>&1
timeout /t 3 /nobreak >nul
echo    Done!

echo.
echo [4/5] Building UI...
dotnet build -c Debug >nul 2>&1
echo    Done!

echo.
echo [5/5] Starting UI...
start "" "bin\Debug\net8.0-windows\RGUI.exe"
timeout /t 2 /nobreak >nul
echo    Done!

echo.
echo === RansomGuard Started ===
echo.
echo Service Status:
sc query RGService | findstr "STATE"
echo.
echo LAN Discovery: ENABLED
echo Broadcast Port: 47700 (UDP)
echo.
echo Check the UI for discovered LAN peers!
echo Logs: C:\ProgramData\RGCoreEssentials\Logs\sentinel_engine.log
echo.
pause
