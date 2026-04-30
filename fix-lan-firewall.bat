@echo off
:: Fix LAN Discovery Firewall Rules
:: Run as Administrator

echo === Configuring Firewall for LAN Discovery ===
echo.

:: Check for admin
net session >nul 2>&1
if %errorLevel% neq 0 (
    echo ERROR: Run as Administrator!
    pause
    exit /b 1
)

echo Adding firewall rules...
netsh advfirewall firewall delete rule name="RansomGuard LAN Discovery" >nul 2>&1
netsh advfirewall firewall add rule name="RansomGuard LAN Discovery" dir=in action=allow protocol=UDP localport=47700 profile=private,domain
netsh advfirewall firewall add rule name="RansomGuard LAN Discovery (Outbound)" dir=out action=allow protocol=UDP localport=47700 profile=private,domain

echo.
echo Restarting service...
net stop RGService >nul 2>&1
timeout /t 2 /nobreak >nul
net start RGService

echo.
echo === Done ===
echo Firewall configured for UDP port 47700
echo Service restarted
echo.
echo Check the UI - peers should appear within 5-15 seconds
pause
