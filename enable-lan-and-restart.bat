@echo off
:: Enable LAN Discovery and Restart RansomGuard
:: Run this as Administrator

echo === RansomGuard LAN Discovery Setup ===
echo.

:: Check for admin privileges
net session >nul 2>&1
if %errorLevel% neq 0 (
    echo ERROR: This script requires administrator privileges!
    echo Please right-click and select "Run as Administrator"
    pause
    exit /b 1
)

echo [1/4] Enabling LAN Circuit Breaker in configuration...
powershell -Command "$config = Get-Content 'C:\ProgramData\RGCoreEssentials\config.json' | ConvertFrom-Json; $config.LanCircuitBreakerEnabled = $true; $config | ConvertTo-Json -Depth 10 | Set-Content 'C:\ProgramData\RGCoreEssentials\config.json'"
echo    Done!

echo.
echo [2/4] Configuring Windows Firewall...
netsh advfirewall firewall delete rule name="RansomGuard LAN Discovery" >nul 2>&1
netsh advfirewall firewall add rule name="RansomGuard LAN Discovery" dir=in action=allow protocol=UDP localport=47700 profile=private,domain description="Allows RansomGuard to discover peers on LAN"
netsh advfirewall firewall add rule name="RansomGuard LAN Discovery (Outbound)" dir=out action=allow protocol=UDP localport=47700 profile=private,domain description="Allows RansomGuard to broadcast beacons on LAN"
echo    Done!

echo.
echo [3/4] Restarting RansomGuard service...
net stop RGService >nul 2>&1
timeout /t 2 /nobreak >nul
net start RGService
echo    Done!

echo.
echo [4/4] Verifying configuration...
powershell -Command "$config = Get-Content 'C:\ProgramData\RGCoreEssentials\config.json' | ConvertFrom-Json; Write-Host '   LAN Circuit Breaker:' $config.LanCircuitBreakerEnabled -ForegroundColor Green; Write-Host '   Broadcast Port:' $config.LanBroadcastPort -ForegroundColor Cyan"

echo.
echo === Setup Complete ===
echo.
echo IMPORTANT NOTES:
echo 1. LAN discovery uses UDP broadcasts on port 47700
echo 2. Both systems must be on the same network subnet
echo 3. Peers will appear in the UI within 5-15 seconds
echo 4. Check logs at: C:\ProgramData\RGCoreEssentials\Logs\sentinel_engine.log
echo.
echo TROUBLESHOOTING:
echo - Verify network profile is 'Private' or 'Domain' (not 'Public')
echo - Some WiFi routers have 'AP Isolation' enabled which blocks peer discovery
echo - Check if the other system also has LAN Circuit Breaker enabled
echo.
pause
