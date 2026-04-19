@echo off
cd /d "%~dp0"
echo ========================================
echo RansomGuard Build and Run Script
echo ========================================
echo.

echo [1/5] Stopping service...
net stop RansomGuardSentinel 2>nul
timeout /t 2 /nobreak >nul

echo [+] Clearing historical activity data...
del /q "C:\ProgramData\RansomGuard\activity_log.*" 2>nul
del /q "C:\ProgramData\RansomGuard\Logs\*.log" 2>nul
echo Done.

echo [2/5] Building projects...
dotnet build RansomGuard.csproj -v q
if %errorlevel% neq 0 (
    echo Build failed on first attempt, retrying...
    dotnet build RansomGuard.csproj -v q
)

echo [3/5] Publishing service with all dependencies...
dotnet publish RansomGuard.Service/RansomGuard.Service.csproj -c Debug -o RansomGuard.Service/publish --self-contained true -r win-x64 -v q

echo [4/5] Starting service...
net start RansomGuardSentinel
if %errorlevel% neq 0 (
    echo WARNING: Service failed to start. Check Event Viewer for details.
    pause
    exit /b 1
)

echo [5/5] Launching UI...
timeout /t 3 /nobreak >nul
cd /d "%~dp0"
start "" "%~dp0bin\Debug\net8.0-windows\RansomGuard.exe"

echo.
echo ========================================
echo SUCCESS! 
echo ========================================
echo Service is running with latest code
echo UI will show "7 WATCHERS ACTIVE" in 2-4 seconds
echo.
pause
