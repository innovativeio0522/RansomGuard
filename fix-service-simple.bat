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
echo RansomGuard Service Path Fix
echo ========================================
echo.

echo [1/4] Stopping RGService...
net stop RGService 2>nul
if %errorlevel% equ 0 (
    echo   Service stopped
) else (
    echo   Service was not running
)
echo.

echo [2/4] Removing old service registration...
sc delete RGService
timeout /t 2 /nobreak >nul
echo   Old service removed
echo.

echo [3/4] Finding correct service executable...
set SERVICE_PATH=
if exist "RansomGuard.Service\bin\Debug\net8.0-windows\win-x64\RGService.exe" (
    set SERVICE_PATH=%~dp0RansomGuard.Service\bin\Debug\net8.0-windows\win-x64\RGService.exe
    echo   Found at: RansomGuard.Service\bin\Debug\net8.0-windows\win-x64\
) else if exist "RansomGuard.Service\publish\RGService.exe" (
    set SERVICE_PATH=%~dp0RansomGuard.Service\publish\RGService.exe
    echo   Found at: RansomGuard.Service\publish\
) else (
    echo   ERROR: Could not find RGService.exe!
    echo   Please build the service first.
    pause
    exit /b 1
)
echo.

echo [4/4] Installing service with correct path...
sc create RGService binPath= "%SERVICE_PATH%" start= auto DisplayName= "RansomGuard Sentinel"
if %errorlevel% equ 0 (
    echo   Service installed successfully
    
    sc config RGService obj= LocalSystem
    sc description RGService "Real-time ransomware protection and file system monitoring service"
    
    echo.
    echo ========================================
    echo Service Installation Complete
    echo ========================================
    echo.
    echo Service Details:
    echo   Name: RGService
    echo   Display Name: RansomGuard Sentinel
    echo   Path: %SERVICE_PATH%
    echo   Start Type: Automatic
    echo   Account: LocalSystem
    echo.
    
    echo Attempting to start the service...
    net start RGService
    if %errorlevel% equ 0 (
        echo.
        echo SUCCESS: Service started successfully!
        echo.
    ) else (
        echo.
        echo WARNING: Service could not be started automatically
        echo Check Event Viewer for details: Windows Logs ^> Application
        echo.
    )
) else (
    echo   ERROR: Service installation failed
    echo   Error code: %errorlevel%
)

echo.
pause
