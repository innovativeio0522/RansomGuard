#!/usr/bin/env pwsh
# RansomGuard Configuration Diagnostic Script
# Identifies why configuration is not persisting

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "RansomGuard Config Diagnostic Tool" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

# Step 1: Identify config location
Write-Host "[1/6] Identifying configuration location..." -ForegroundColor Yellow

$isMsix = $null -ne $env:MSIX_PACKAGE_FAMILY_NAME
if ($isMsix) {
    Write-Host "  Running as MSIX package: $env:MSIX_PACKAGE_FAMILY_NAME" -ForegroundColor Green
    $configPath = "$env:LocalAppData\RansomGuard\config.json"
} else {
    Write-Host "  Running as traditional install" -ForegroundColor Green
    $configPath = "$env:ProgramData\RansomGuard\config.json"
}

Write-Host "  Config path: $configPath" -ForegroundColor Cyan
Write-Host ""

# Step 2: Check if config file exists
Write-Host "[2/6] Checking config file..." -ForegroundColor Yellow

if (Test-Path $configPath) {
    Write-Host "  Config file exists" -ForegroundColor Green
    
    $fileInfo = Get-Item $configPath
    Write-Host "  File size: $($fileInfo.Length) bytes" -ForegroundColor Cyan
    Write-Host "  Last modified: $($fileInfo.LastWriteTime)" -ForegroundColor Cyan
    Write-Host "  Attributes: $($fileInfo.Attributes)" -ForegroundColor Cyan
    
    # Try to read it
    try {
        $config = Get-Content $configPath | ConvertFrom-Json
        Write-Host "  Config is valid JSON" -ForegroundColor Green
        Write-Host "  WatchdogEnabled: $($config.WatchdogEnabled)" -ForegroundColor $(if ($config.WatchdogEnabled) { "Green" } else { "Red" })
        Write-Host "  RealTimeProtection: $($config.RealTimeProtection)" -ForegroundColor Cyan
        Write-Host "  SensitivityLevel: $($config.SensitivityLevel)" -ForegroundColor Cyan
        Write-Host "  MonitoredPaths: $($config.MonitoredPaths.Count) paths" -ForegroundColor Cyan
    } catch {
        Write-Host "  Config is corrupted: $($_.Exception.Message)" -ForegroundColor Red
    }
} else {
    Write-Host "  Config file does not exist!" -ForegroundColor Red
    Write-Host "  The app will create it on first run" -ForegroundColor Yellow
}
Write-Host ""

# Step 3: Check write permissions
Write-Host "[3/6] Testing write permissions..." -ForegroundColor Yellow

$testFile = Join-Path (Split-Path $configPath) "test_write.tmp"
try {
    "test" | Out-File $testFile -ErrorAction Stop
    Remove-Item $testFile -ErrorAction Stop
    Write-Host "  Write access OK" -ForegroundColor Green
} catch {
    Write-Host "  No write access: $($_.Exception.Message)" -ForegroundColor Red
    Write-Host "  Try running as Administrator" -ForegroundColor Yellow
}
Write-Host ""

# Step 4: Check for running processes
Write-Host "[4/6] Checking for running processes..." -ForegroundColor Yellow

$processes = Get-Process | Where-Object { 
    $_.ProcessName -like "*Maintenance*" -or 
    $_.ProcessName -like "*RansomGuard*" 
}

if ($processes) {
    Write-Host "  Found $($processes.Count) RansomGuard process(es):" -ForegroundColor Yellow
    foreach ($proc in $processes) {
        Write-Host "    - $($proc.ProcessName) (PID: $($proc.Id), Started: $($proc.StartTime))" -ForegroundColor Cyan
    }
    
    if ($processes.Count -gt 3) {
        Write-Host "  WARNING: Multiple processes detected! They might be fighting over config." -ForegroundColor Red
    }
} else {
    Write-Host "  No RansomGuard processes running" -ForegroundColor Gray
}
Write-Host ""

# Step 5: Check for duplicate config files
Write-Host "[5/6] Searching for duplicate config files..." -ForegroundColor Yellow

$possiblePaths = @(
    "$env:LocalAppData\RansomGuard\config.json",
    "$env:ProgramData\RansomGuard\config.json",
    "$env:AppData\RansomGuard\config.json",
    ".\config.json",
    ".\bin\Debug\net8.0-windows\config.json"
)

$foundConfigs = @()
foreach ($path in $possiblePaths) {
    if (Test-Path $path) {
        $foundConfigs += $path
        $size = (Get-Item $path).Length
        $modified = (Get-Item $path).LastWriteTime
        Write-Host "  Found: $path" -ForegroundColor Cyan
        Write-Host "     Size: $size bytes, Modified: $modified" -ForegroundColor Gray
    }
}

if ($foundConfigs.Count -gt 1) {
    Write-Host "  WARNING: Multiple config files found! App might be using wrong one." -ForegroundColor Red
} elseif ($foundConfigs.Count -eq 1) {
    Write-Host "  Only one config file found (correct)" -ForegroundColor Green
} else {
    Write-Host "  No config files found" -ForegroundColor Gray
}
Write-Host ""

# Step 6: Check logs for errors
Write-Host "[6/6] Checking application logs..." -ForegroundColor Yellow

$logDir = Join-Path (Split-Path $configPath) "Logs"
if (Test-Path $logDir) {
    Write-Host "  Log directory: $logDir" -ForegroundColor Cyan
    
    $logFiles = Get-ChildItem $logDir -Filter "*.log" -ErrorAction SilentlyContinue
    if ($logFiles) {
        Write-Host "  Found $($logFiles.Count) log file(s)" -ForegroundColor Cyan
        
        # Check for config-related errors in recent logs
        $recentErrors = Get-ChildItem $logDir -Filter "*.log" | 
            Get-Content | 
            Select-String -Pattern "ConfigurationService|ERROR|Exception|config.json" | 
            Select-Object -Last 10
        
        if ($recentErrors) {
            Write-Host "  Recent config-related log entries:" -ForegroundColor Yellow
            foreach ($line in $recentErrors) {
                Write-Host "    $line" -ForegroundColor Gray
            }
        } else {
            Write-Host "  No recent config errors in logs" -ForegroundColor Green
        }
    } else {
        Write-Host "  No log files found" -ForegroundColor Gray
    }
} else {
    Write-Host "  Log directory does not exist: $logDir" -ForegroundColor Gray
}
Write-Host ""

# Summary
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "Summary & Recommendations" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan

if (Test-Path $configPath) {
    try {
        $config = Get-Content $configPath | ConvertFrom-Json
        
        Write-Host "Current Configuration:" -ForegroundColor White
        Write-Host "  WatchdogEnabled: $($config.WatchdogEnabled)" -ForegroundColor $(if ($config.WatchdogEnabled) { "Green" } else { "Red" })
        Write-Host ""
        
        if (-not $config.WatchdogEnabled) {
            Write-Host "To enable watchdog:" -ForegroundColor Yellow
            Write-Host "   1. Open RansomGuard Settings" -ForegroundColor White
            Write-Host "   2. Toggle Self-healing Protection ON" -ForegroundColor White
            Write-Host "   3. Run this script again to verify" -ForegroundColor White
        }
    } catch {
        Write-Host "Could not read config file" -ForegroundColor Red
    }
}

Write-Host ""
Write-Host "For detailed analysis, see: Markdown\CONFIG_SAVE_ANALYSIS.md" -ForegroundColor Cyan
Write-Host ""
