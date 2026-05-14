@echo off
:: Uninstall RansomGuard Services
:: Run this as Administrator

echo === Uninstalling RansomGuard Services ===
echo.

:: Check for admin privileges
net session >nul 2>&1
if %errorLevel% neq 0 (
    echo ERROR: This script requires administrator privileges!
    echo Please right-click and select "Run as Administrator"
    echo.
    pause
    exit /b 1
)

echo [1/1] Uninstalling RGService...
sc query RGService >nul 2>&1
if %errorLevel% equ 0 (
    sc stop RGService >nul 2>&1
    timeout /t 2 /nobreak >nul
    sc delete RGService
    echo    Done!
) else (
    echo    Service not found
)

echo.
echo === Verification ===
sc query RGService >nul 2>&1
if %errorLevel% equ 0 (
    echo RGService: Still installed
) else (
    echo RGService: Uninstalled
)

echo.
echo === Complete ===
echo All RansomGuard services have been uninstalled.
echo.
pause
