@echo off
echo Stopping service if running...
net stop RansomGuardSentinel 2>nul

echo.
echo Starting RansomGuard Sentinel Service...
net start RansomGuardSentinel

if %errorlevel% == 0 (
    echo.
    echo ========================================
    echo Service started successfully!
    echo ========================================
    echo.
    echo The WPF app should automatically reconnect
    echo and show "7 WATCHERS ACTIVE" within 2-4 seconds.
    echo.
) else (
    echo.
    echo Failed to start service. Error code: %errorlevel%
    echo.
)

pause
