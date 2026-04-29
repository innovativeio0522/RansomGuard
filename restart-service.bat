@echo off
cd /d "%~dp0"

:: Check for admin privileges
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

echo ========================================
echo Restarting RansomGuard Service
echo ========================================
echo.

echo [1/2] Stopping service...
net stop RGService
timeout /t 2 /nobreak >nul
echo   Service stopped
echo.

echo [2/2] Starting service...
net start RGService
if %errorlevel% equ 0 (
    echo   Service started successfully!
    echo.
    echo Service is now running with pipe security fix.
    echo The UI should connect automatically.
) else (
    echo   ERROR: Service failed to start!
    echo   Check Event Viewer for details.
)

echo.
pause
