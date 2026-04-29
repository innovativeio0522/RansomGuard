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
echo Rebuild and Restart RansomGuard Service
echo ========================================
echo.

echo [1/4] Stopping service...
net stop RGService
timeout /t 3 /nobreak >nul
echo   Service stopped
echo.

echo [2/4] Rebuilding service with pipe security fix...
dotnet publish RansomGuard.Service/RansomGuard.Service.csproj -c Debug -r win-x64 --self-contained true -o RansomGuard.Service/bin/Debug/net8.0-windows/win-x64 -v q
if %errorlevel% neq 0 (
    echo   ERROR: Build failed!
    pause
    exit /b 1
)
echo   Service rebuilt successfully
echo.

echo [3/4] Verifying System.IO.Pipes.AccessControl.dll...
if exist "RansomGuard.Service\bin\Debug\net8.0-windows\win-x64\System.IO.Pipes.AccessControl.dll" (
    echo   Pipe security DLL found
) else (
    echo   WARNING: Pipe security DLL not found!
)
echo.

echo [4/4] Starting service...
net start RGService
if %errorlevel% equ 0 (
    echo   Service started successfully!
    echo.
    echo ========================================
    echo Service is now running with pipe security fix!
    echo ========================================
    echo.
    echo The UI should connect automatically within a few seconds.
    echo.
    echo Checking connection...
    timeout /t 5 /nobreak >nul
    
    findstr /C:"Pipe created with Everyone access" "C:\ProgramData\RGCoreEssentials\Logs\ipc.log" >nul 2>&1
    if %errorlevel% equ 0 (
        echo SUCCESS: Pipe security is active!
    ) else (
        echo WARNING: Could not verify pipe security in logs.
    )
) else (
    echo   ERROR: Service failed to start!
    echo   Check Event Viewer for details.
)

echo.
pause
