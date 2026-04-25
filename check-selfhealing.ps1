# RansomGuard Self-Healing Diagnostic Script
# Run this script to diagnose why self-healing is not working

Write-Host "=== RansomGuard Self-Healing Diagnostic ===" -ForegroundColor Cyan
Write-Host ""

# 1. Check if Watchdog process is running
Write-Host "[1] Checking Watchdog Process..." -ForegroundColor Yellow
$watchdogProcess = Get-Process -Name "MaintenanceWorker" -ErrorAction SilentlyContinue
if ($watchdogProcess) {
    Write-Host "    ✓ Watchdog is RUNNING (PID: $($watchdogProcess.Id))" -ForegroundColor Green
    Write-Host "    Path: $($watchdogProcess.Path)" -ForegroundColor Gray
} else {
    Write-Host "    ✗ Watchdog is NOT RUNNING" -ForegroundColor Red
}
Write-Host ""

# 2. Check if UI process is running
Write-Host "[2] Checking UI Process..." -ForegroundColor Yellow
$uiProcess = Get-Process -Name "MaintenanceUI" -ErrorAction SilentlyContinue
if ($uiProcess) {
    Write-Host "    ✓ UI is RUNNING (PID: $($uiProcess.Id))" -ForegroundColor Green
    Write-Host "    Path: $($uiProcess.Path)" -ForegroundColor Gray
} else {
    Write-Host "    ✗ UI is NOT RUNNING" -ForegroundColor Red
}
Write-Host ""

# 3. Check if Service is running
Write-Host "[3] Checking Service..." -ForegroundColor Yellow
$service = Get-Service -Name "WinMaintenance" -ErrorAction SilentlyContinue
if ($service) {
    if ($service.Status -eq "Running") {
        Write-Host "    ✓ Service is RUNNING" -ForegroundColor Green
    } else {
        Write-Host "    ✗ Service is $($service.Status)" -ForegroundColor Red
    }
} else {
    Write-Host "    ✗ Service NOT FOUND (not installed)" -ForegroundColor Red
}
Write-Host ""

# 4. Check configuration
Write-Host "[4] Checking Configuration..." -ForegroundColor Yellow
$configPath = "$env:ProgramData\RansomGuard\config.json"
if (Test-Path $configPath) {
    Write-Host "    ✓ Config file exists: $configPath" -ForegroundColor Green
    try {
        $config = Get-Content $configPath | ConvertFrom-Json
        if ($config.WatchdogEnabled -eq $true) {
            Write-Host "    ✓ WatchdogEnabled = TRUE" -ForegroundColor Green
        } else {
            Write-Host "    ✗ WatchdogEnabled = FALSE (Self-healing is DISABLED)" -ForegroundColor Red
            Write-Host "    → Enable it in Settings > Self-healing Protection" -ForegroundColor Yellow
        }
    } catch {
        Write-Host "    ✗ Failed to read config: $_" -ForegroundColor Red
    }
} else {
    Write-Host "    ✗ Config file NOT FOUND: $configPath" -ForegroundColor Red
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
    if (Test-Path "$path\MaintenanceUI.exe") {
        $foundInstall = $true
        Write-Host "    ✓ Installation found: $path" -ForegroundColor Green
        
        # Check for MaintenanceWorker.exe
        if (Test-Path "$path\MaintenanceWorker.exe") {
            Write-Host "    ✓ MaintenanceWorker.exe exists" -ForegroundColor Green
        } else {
            Write-Host "    ✗ MaintenanceWorker.exe NOT FOUND" -ForegroundColor Red
            Write-Host "    → This is the problem! Watchdog executable is missing." -ForegroundColor Yellow
            Write-Host "    → Copy MaintenanceWorker.exe to: $path" -ForegroundColor Yellow
        }
        
        # Check for WinMaintenanceSvc.exe
        if (Test-Path "$path\WinMaintenanceSvc.exe") {
            Write-Host "    ✓ WinMaintenanceSvc.exe exists" -ForegroundColor Green
        } else {
            Write-Host "    ✗ WinMaintenanceSvc.exe NOT FOUND" -ForegroundColor Red
        }
        break
    }
}

if (-not $foundInstall) {
    Write-Host "    ✗ Installation directory NOT FOUND" -ForegroundColor Red
    Write-Host "    Searched in:" -ForegroundColor Gray
    foreach ($path in $installPaths) {
        Write-Host "      - $path" -ForegroundColor Gray
    }
}
Write-Host ""

# 6. Check Debug Output (if available)
Write-Host "[6] Checking Debug Output..." -ForegroundColor Yellow
$debugLogPath = "$env:ProgramData\RansomGuard\Logs\watchdog.log"
if (Test-Path $debugLogPath) {
    Write-Host "    ✓ Debug log exists: $debugLogPath" -ForegroundColor Green
    Write-Host "    Last 10 lines:" -ForegroundColor Gray
    Get-Content $debugLogPath -Tail 10 | ForEach-Object { Write-Host "      $_" -ForegroundColor Gray }
} else {
    Write-Host "    ℹ Debug log not found (this is normal)" -ForegroundColor Gray
}
Write-Host ""

# 7. Recommendations
Write-Host "=== RECOMMENDATIONS ===" -ForegroundColor Cyan
Write-Host ""

if (-not $watchdogProcess) {
    Write-Host "→ Watchdog is not running. Possible causes:" -ForegroundColor Yellow
    Write-Host "  1. MaintenanceWorker.exe is missing from installation directory" -ForegroundColor White
    Write-Host "  2. Self-healing is disabled in Settings" -ForegroundColor White
    Write-Host "  3. Watchdog crashed on startup (check Event Viewer)" -ForegroundColor White
    Write-Host ""
    
    # Check if executable is missing
    $foundExe = $false
    foreach ($path in $installPaths) {
        if (Test-Path "$path\MaintenanceUI.exe") {
            if (-not (Test-Path "$path\MaintenanceWorker.exe")) {
                Write-Host "→ QUICK FIX - Build and copy Watchdog:" -ForegroundColor Yellow
                Write-Host "  1. cd RansomGuard.Watchdog" -ForegroundColor White
                Write-Host "  2. dotnet build -c Debug" -ForegroundColor White
                Write-Host "  3. copy bin\Debug\net8.0\MaintenanceWorker.exe ..\$path\" -ForegroundColor White
                Write-Host "  4. Restart MaintenanceUI.exe" -ForegroundColor White
                Write-Host ""
                Write-Host "→ PERMANENT FIX - Use updated build script:" -ForegroundColor Yellow
                Write-Host "  The build-and-run.bat script has been updated to automatically" -ForegroundColor White
                Write-Host "  build and copy the Watchdog. Just run: .\build-and-run.bat" -ForegroundColor White
                $foundExe = $true
                break
            }
        }
    }
    
    if (-not $foundExe) {
        Write-Host "→ To fix:" -ForegroundColor Yellow
        Write-Host "  1. Ensure MaintenanceWorker.exe is in the same folder as MaintenanceUI.exe" -ForegroundColor White
        Write-Host "  2. Enable 'Self-healing Protection' in Settings" -ForegroundColor White
        Write-Host "  3. Restart the application" -ForegroundColor White
    }
    Write-Host ""
}

if ($watchdogProcess -and -not $service) {
    Write-Host "→ Service is not installed. To install:" -ForegroundColor Yellow
    Write-Host "  Run as Administrator: sc.exe create WinMaintenance binPath= `"<path>\WinMaintenanceSvc.exe`"" -ForegroundColor White
    Write-Host ""
}

# Additional guidance
Write-Host "→ For detailed troubleshooting, see:" -ForegroundColor Cyan
Write-Host "  Markdown\SELF_HEALING_TROUBLESHOOTING.md" -ForegroundColor White
Write-Host ""

Write-Host "=== END OF DIAGNOSTIC ===" -ForegroundColor Cyan
Write-Host ""
Write-Host "Press any key to exit..."
$null = $Host.UI.RawUI.ReadKey("NoEcho,IncludeKeyDown")
