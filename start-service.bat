@echo off
echo Starting RansomGuard Sentinel Service...
net start RansomGuardSentinel
if %errorlevel% == 0 (
    echo.
    echo Service started successfully!
    echo The WPF app should show "7 WATCHERS ACTIVE" within 2-4 seconds.
) else (
    echo.
    echo Failed to start service. Error code: %errorlevel%
)
echo.
pause
