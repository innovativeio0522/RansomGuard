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
echo Update and Restart RansomGuard Service
echo ========================================
echo.

echo [1/3] Stopping service...
net stop RGService
timeout /t 3 /nobreak >nul
echo   Service stopped
echo.

echo [2/3] Updating service executable...
copy /Y "RansomGuard.Service\bin\Debug\net8.0-windows\RGService.exe" "RansomGuard.Service\bin\Debug\net8.0-windows\win-x64\RGService.exe" >nul
if %errorlevel% equ 0 (
    echo   Service executable updated
) else (
    echo   ERROR: Failed to copy service executable!
    pause
    exit /b 1
)
echo.

echo [3/3] Starting service with pipe security fix...
net start RGService
if %errorlevel% equ 0 (
    echo   Service started successfully!
    echo.
    echo ========================================
    echo Service is now running with pipe security fix!
    echo ========================================
    echo.
    echo The UI should connect automatically within a few seconds.
    echo Check the Dashboard to verify connection status.
) else (
    echo   ERROR: Service failed to start!
    echo   Check Event Viewer for details.
)

echo.
pause
