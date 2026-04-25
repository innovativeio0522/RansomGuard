# RansomGuard Installation Verification Script
# Verifies that all required executables are present after build

param(
    [string]$BuildPath = "bin\Debug\net8.0-windows"
)

Write-Host "=== RansomGuard Installation Verification ===" -ForegroundColor Cyan
Write-Host ""
Write-Host "Checking build directory: $BuildPath" -ForegroundColor Gray
Write-Host ""

$allGood = $true

# Required executables
$requiredFiles = @(
    @{Name = "MaintenanceUI.exe"; Description = "Main UI Application"; Critical = $true},
    @{Name = "MaintenanceWorker.exe"; Description = "Self-Healing Watchdog"; Critical = $true},
    @{Name = "RansomGuard.Core.dll"; Description = "Core Library"; Critical = $true}
)

# Optional but recommended files
$optionalFiles = @(
    @{Name = "WinMaintenanceSvc.exe"; Description = "Background Service"; Critical = $false}
)

Write-Host "[1] Checking Required Files..." -ForegroundColor Yellow
foreach ($file in $requiredFiles) {
    $path = Join-Path $BuildPath $file.Name
    if (Test-Path $path) {
        $fileInfo = Get-Item $path
        $sizeKB = [math]::Round($fileInfo.Length / 1KB, 2)
        Write-Host "    ✓ $($file.Name) - $sizeKB KB" -ForegroundColor Green
        Write-Host "      $($file.Description)" -ForegroundColor Gray
    } else {
        Write-Host "    ✗ $($file.Name) - MISSING!" -ForegroundColor Red
        Write-Host "      $($file.Description)" -ForegroundColor Gray
        $allGood = $false
    }
}
Write-Host ""

Write-Host "[2] Checking Optional Files..." -ForegroundColor Yellow
foreach ($file in $optionalFiles) {
    $path = Join-Path $BuildPath $file.Name
    if (Test-Path $path) {
        $fileInfo = Get-Item $path
        $sizeKB = [math]::Round($fileInfo.Length / 1KB, 2)
        Write-Host "    ✓ $($file.Name) - $sizeKB KB" -ForegroundColor Green
        Write-Host "      $($file.Description)" -ForegroundColor Gray
    } else {
        Write-Host "    ℹ $($file.Name) - Not found (optional)" -ForegroundColor Yellow
        Write-Host "      $($file.Description)" -ForegroundColor Gray
    }
}
Write-Host ""

# Check if processes are running
Write-Host "[3] Checking Running Processes..." -ForegroundColor Yellow
$uiProcess = Get-Process -Name "MaintenanceUI" -ErrorAction SilentlyContinue
$watchdogProcess = Get-Process -Name "MaintenanceWorker" -ErrorAction SilentlyContinue

if ($uiProcess) {
    Write-Host "    ⚠ MaintenanceUI.exe is currently RUNNING (PID: $($uiProcess.Id))" -ForegroundColor Yellow
    Write-Host "      You may need to restart it to use the new build" -ForegroundColor Gray
} else {
    Write-Host "    ℹ MaintenanceUI.exe is not running" -ForegroundColor Gray
}

if ($watchdogProcess) {
    Write-Host "    ⚠ MaintenanceWorker.exe is currently RUNNING (PID: $($watchdogProcess.Id))" -ForegroundColor Yellow
    Write-Host "      You may need to restart it to use the new build" -ForegroundColor Gray
} else {
    Write-Host "    ℹ MaintenanceWorker.exe is not running" -ForegroundColor Gray
}
Write-Host ""

# Check configuration
Write-Host "[4] Checking Configuration..." -ForegroundColor Yellow
$configPath = "$env:ProgramData\RansomGuard\config.json"
if (Test-Path $configPath) {
    Write-Host "    ✓ Configuration file exists" -ForegroundColor Green
    try {
        $config = Get-Content $configPath | ConvertFrom-Json
        if ($config.PSObject.Properties.Name -contains "WatchdogEnabled") {
            if ($config.WatchdogEnabled -eq $true) {
                Write-Host "      Self-healing: ENABLED" -ForegroundColor Green
            } else {
                Write-Host "      Self-healing: DISABLED" -ForegroundColor Yellow
            }
        } else {
            Write-Host "      Self-healing: Not configured (will default to enabled)" -ForegroundColor Gray
        }
    } catch {
        Write-Host "    ⚠ Could not read configuration" -ForegroundColor Yellow
    }
} else {
    Write-Host "    ℹ Configuration file not found (will be created on first run)" -ForegroundColor Gray
}
Write-Host ""

# Final verdict
Write-Host "=== VERIFICATION RESULT ===" -ForegroundColor Cyan
if ($allGood) {
    Write-Host "✓ All required files are present!" -ForegroundColor Green
    Write-Host ""
    Write-Host "You can now run the application:" -ForegroundColor White
    Write-Host "  $BuildPath\MaintenanceUI.exe" -ForegroundColor Cyan
    Write-Host ""
    Write-Host "Self-healing will automatically start when you launch the UI." -ForegroundColor Gray
} else {
    Write-Host "✗ Some required files are missing!" -ForegroundColor Red
    Write-Host ""
    Write-Host "To fix missing files:" -ForegroundColor Yellow
    Write-Host "  1. Run: .\build-and-run.bat" -ForegroundColor White
    Write-Host "     (This will build all projects and copy required files)" -ForegroundColor Gray
    Write-Host ""
    Write-Host "  OR manually build missing components:" -ForegroundColor Yellow
    Write-Host "  - For MaintenanceWorker.exe:" -ForegroundColor White
    Write-Host "    cd RansomGuard.Watchdog" -ForegroundColor Gray
    Write-Host "    dotnet build -c Debug" -ForegroundColor Gray
    Write-Host "    copy bin\Debug\net8.0\MaintenanceWorker.exe ..\$BuildPath\" -ForegroundColor Gray
}
Write-Host ""
Write-Host "=== END OF VERIFICATION ===" -ForegroundColor Cyan
Write-Host ""
