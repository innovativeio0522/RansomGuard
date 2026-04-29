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
echo RansomGuard - Complete Setup and Run
echo ========================================
echo.

:: Step 1: Clear database and logs
echo [1/6] Clearing database and logs...
del /q "C:\ProgramData\RGCoreEssentials\activity_log.*" 2>nul
del /q "C:\ProgramData\RGCoreEssentials\Logs\*.log" 2>nul
del /q "C:\ProgramData\RGCoreEssentials\Quarantine\*.quarantine" 2>nul
echo   Database and logs cleared
echo.

:: Step 2: Stop and remove old service
echo [2/6] Stopping and removing old service...
net stop RGService 2>nul
sc delete RGService 2>nul
timeout /t 2 /nobreak >nul
echo   Old service removed
echo.

:: Step 3: Find service executable
echo [3/6] Locating service executable...
set SERVICE_PATH=
if exist "RansomGuard.Service\bin\Debug\net8.0-windows\win-x64\RGService.exe" (
    set SERVICE_PATH=%~dp0RansomGuard.Service\bin\Debug\net8.0-windows\win-x64\RGService.exe
    echo   Found at: RansomGuard.Service\bin\Debug\net8.0-windows\win-x64\
) else if exist "RansomGuard.Service\publish\RGService.exe" (
    set SERVICE_PATH=%~dp0RansomGuard.Service\publish\RGService.exe
    echo   Found at: RansomGuard.Service\publish\
) else (
    echo   ERROR: Could not find RGService.exe!
    echo   Building service now...
    dotnet build RansomGuard.Service\RansomGuard.Service.csproj -c Debug -v q
    if exist "RansomGuard.Service\bin\Debug\net8.0-windows\win-x64\RGService.exe" (
        set SERVICE_PATH=%~dp0RansomGuard.Service\bin\Debug\net8.0-windows\win-x64\RGService.exe
        echo   Service built successfully
    ) else (
        echo   ERROR: Build failed!
        pause
        exit /b 1
    )
)
echo.

:: Step 4: Install service
echo [4/6] Installing RansomGuard service...
sc create RGService binPath= "%SERVICE_PATH%" start= auto DisplayName= "RansomGuard Sentinel"
if %errorlevel% neq 0 (
    echo   ERROR: Service installation failed!
    pause
    exit /b 1
)
sc config RGService obj= LocalSystem
sc description RGService "Real-time ransomware protection and file system monitoring service"
echo   Service installed successfully
echo.

:: Step 5: Start service
echo [5/6] Starting RansomGuard service...
net start RGService
if %errorlevel% neq 0 (
    echo   ERROR: Service failed to start!
    echo   Check Event Viewer for details.
    pause
    exit /b 1
)
echo   Service started successfully
echo.

:: Step 6: Launch UI
echo [6/6] Launching RansomGuard UI...
timeout /t 2 /nobreak >nul

if exist "bin\Debug\net8.0-windows\RGUI.exe" (
    start "" "%~dp0bin\Debug\net8.0-windows\RGUI.exe"
    echo   UI launched
) else (
    echo   ERROR: RGUI.exe not found!
    echo   Build the UI project first.
    pause
    exit /b 1
)

echo.
echo ========================================
echo RansomGuard is now running!
echo ========================================
echo.
echo Service Status:
net query RGService
echo.
echo The UI should appear in your system tray shortly.
echo.
pause
