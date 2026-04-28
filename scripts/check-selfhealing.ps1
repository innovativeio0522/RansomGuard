# RansomGuard Self-Healing Diagnostic Script
# Run this script to diagnose why self-healing is not working

Write-Host "=== RansomGuard Self-Healing Diagnostic ===" -ForegroundColor Cyan
Write-Host ""

# 1. Check if Watchdog process is running
Write-Host "[1] Checking Watchdog Process..." -ForegroundColor Yellow
$watchdogProcess = Get-Process -Name "RGWorker" -ErrorAction SilentlyContinue
if ($watchdogProcess) {
    Write-Host "    [OK] Watchdog is RUNNING (PID: $($watchdogProcess.Id))" -ForegroundColor Green
    Write-Host "    Path: $($watchdogProcess.Path)" -ForegroundColor Gray
} else {
    Write-Host "    [X] Watchdog is NOT RUNNING" -ForegroundColor Red
}
Write-Host ""

# 2. Check if UI process is running
Write-Host "[2] Checking UI Process..." -ForegroundColor Yellow
$uiProcess = Get-Process -Name "RGUI" -ErrorAction SilentlyContinue
if ($uiProcess) {
    Write-Host "    [OK] UI is RUNNING (PID: $($uiProcess.Id))" -ForegroundColor Green
    Write-Host "    Path: $($uiProcess.Path)" -ForegroundColor Gray
} else {
    Write-Host "    [X] UI is NOT RUNNING" -ForegroundColor Red
}
Write-Host ""

# 3. Check if Service is running
Write-Host "[3] Checking Service..." -ForegroundColor Yellow
$service = Get-Service -Name "RGService" -ErrorAction SilentlyContinue
if ($service) {
    if ($service.Status -eq "Running") {
        Write-Host "    [OK] Service is RUNNING" -ForegroundColor Green
    } else {
        Write-Host "    [X] Service is $($service.Status)" -ForegroundColor Red
    }
} else {
    Write-Host "    [X] Service NOT FOUND (not installed)" -ForegroundColor Red
}
Write-Host ""

# 4. Check configuration
Write-Host "[4] Checking Configuration..." -ForegroundColor Yellow
$configPath = "$env:ProgramData\RGCoreEssentials\config.json"
if (Test-Path $configPath) {
    Write-Host "    [OK] Config file exists: $configPath" -ForegroundColor Green
    try {
        $config = Get-Content $configPath | ConvertFrom-Json
        if ($config.WatchdogEnabled -eq $true) {
            Write-Host "    [OK] WatchdogEnabled = TRUE" -ForegroundColor Green
        } else {
            Write-Host "    [X] WatchdogEnabled = FALSE (Self-healing is DISABLED)" -ForegroundColor Red
            Write-Host "    (!) Enable it in Settings > Self-healing Protection" -ForegroundColor Yellow
        }
    } catch {
        Write-Host "    [X] Failed to read config: $_" -ForegroundColor Red
    }
} else {
    Write-Host "    [X] Config file NOT FOUND: $configPath" -ForegroundColor Red
}
Write-Host ""

# 5. Check if executables exist
Write-Host "[5] Checking Executable Files..." -ForegroundColor Yellow

# Find installation directory
$installPaths = @(
    "$env:ProgramFiles\RansomGuard",
    "$env:LOCALAPPDATA\RansomGuard",
    ".\bin\Debug\net8.0-windows",
    ".\bin\Release\net8.0-windows"
)

$foundInstall = $false
foreach ($path in $installPaths) {
    if (Test-Path "$path\RGUI.exe") {
        $foundInstall = $true
        Write-Host "    [OK] Installation found: $path" -ForegroundColor Green
        
        # Check for RGWorker.exe
        if (Test-Path "$path\RGWorker.exe") {
            Write-Host "    [OK] RGWorker.exe exists" -ForegroundColor Green
        } else {
            Write-Host "    [X] RGWorker.exe NOT FOUND" -ForegroundColor Red
            Write-Host "    (!) This is the problem! Watchdog executable is missing." -ForegroundColor Yellow
            Write-Host "    (!) Copy RGWorker.exe to: $path" -ForegroundColor Yellow
        }
        
        # Check for RGService.exe
        if (Test-Path "$path\RGService.exe") {
            Write-Host "    [OK] RGService.exe exists" -ForegroundColor Green
        } else {
            Write-Host "    [X] RGService.exe NOT FOUND" -ForegroundColor Red
        }
        break
    }
}

if (-not $foundInstall) {
    Write-Host "    [X] Installation directory NOT FOUND" -ForegroundColor Red
    Write-Host "    Searched in:" -ForegroundColor Gray
    foreach ($path in $installPaths) {
        Write-Host "      - $path" -ForegroundColor Gray
    }
}
Write-Host ""

# 6. Check Debug Output (if available)
Write-Host "[6] Checking Debug Output..." -ForegroundColor Yellow
$debugLogPath = "$env:ProgramData\RGCoreEssentials\Logs\watchdog.log"
if (Test-Path $debugLogPath) {
    Write-Host "    [OK] Debug log exists: $debugLogPath" -ForegroundColor Green
    Write-Host "    Last 10 lines:" -ForegroundColor Gray
    Get-Content $debugLogPath -Tail 10 | ForEach-Object { Write-Host "      $_" -ForegroundColor Gray }
} else {
    Write-Host "    (i) Debug log not found (this is normal)" -ForegroundColor Gray
}
Write-Host ""

# 7. Recommendations
Write-Host "=== RECOMMENDATIONS ===" -ForegroundColor Cyan
Write-Host ""

if (-not $watchdogProcess) {
    Write-Host "(!) Watchdog is not running. Possible causes:" -ForegroundColor Yellow
    Write-Host "  1. RGWorker.exe is missing from installation directory" -ForegroundColor White
    Write-Host "  2. Self-healing is disabled in Settings" -ForegroundColor White
    Write-Host "  3. Watchdog crashed on startup (check Event Viewer)" -ForegroundColor White
    Write-Host ""
    
    # Check if executable is missing
    $foundExe = $false
    foreach ($path in $installPaths) {
        if (Test-Path "$path\RGUI.exe") {
            if (-not (Test-Path "$path\RGWorker.exe")) {
                Write-Host "(!) QUICK FIX - Build and copy Watchdog:" -ForegroundColor Yellow
                Write-Host "  1. cd RansomGuard.Watchdog" -ForegroundColor White
                Write-Host "  2. dotnet build -c Debug" -ForegroundColor White
                Write-Host "  3. copy bin\Debug\net8.0\RGWorker.exe ..\$path\" -ForegroundColor White
                Write-Host "  4. Restart RGUI.exe" -ForegroundColor White
                Write-Host ""
                Write-Host "(!) PERMANENT FIX - Use updated build script:" -ForegroundColor Yellow
                Write-Host "  The build-and-run.bat script has been updated to automatically" -ForegroundColor White
                Write-Host "  build and copy the Watchdog. Just run: .\build-and-run.bat" -ForegroundColor White
                $foundExe = $true
                break
            }
        }
    }
    
    if (-not $foundExe) {
        Write-Host "(!) To fix:" -ForegroundColor Yellow
        Write-Host "  1. Ensure RGWorker.exe is in the same folder as RGUI.exe" -ForegroundColor White
        Write-Host "  2. Enable 'Self-healing Protection' in Settings" -ForegroundColor White
        Write-Host "  3. Restart the application" -ForegroundColor White
    }
    Write-Host ""
}

if ($watchdogProcess -and -not $service) {
    Write-Host "(!) Service is not installed. To install:" -ForegroundColor Yellow
    Write-Host "  Run as Administrator: sc.exe create RGService binPath= `"<path>\RGService.exe`"" -ForegroundColor White
    Write-Host ""
}

# Additional guidance
Write-Host "(!) For detailed troubleshooting, see:" -ForegroundColor Cyan
Write-Host "  Markdown\SELF_HEALING_TROUBLESHOOTING.md" -ForegroundColor White
Write-Host ""

Write-Host "=== END OF DIAGNOSTIC ===" -ForegroundColor Cyan
Write-Host ""
Write-Host "Press any key to exit..."
# $null = $Host.UI.RawUI.ReadKey("NoEcho,IncludeKeyDown") # Disabled for automation
