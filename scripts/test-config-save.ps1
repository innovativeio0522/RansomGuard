#!/usr/bin/env pwsh
# Test script to manually enable watchdog in config

$configPath = "C:\ProgramData\RansomGuard\config.json"

Write-Host "Testing config save..." -ForegroundColor Cyan
Write-Host ""

# Backup current config
$backupPath = "$configPath.backup"
Copy-Item $configPath $backupPath -Force
Write-Host "Backed up config to: $backupPath" -ForegroundColor Green

# Read current config
$config = Get-Content $configPath | ConvertFrom-Json
Write-Host "Current WatchdogEnabled: $($config.WatchdogEnabled)" -ForegroundColor Yellow

# Set WatchdogEnabled to true
$config.WatchdogEnabled = $true
Write-Host "Setting WatchdogEnabled to: True" -ForegroundColor Green

# Save config
$config | ConvertTo-Json -Depth 10 | Set-Content $configPath
Write-Host "Saved config" -ForegroundColor Green
Write-Host ""

# Wait a moment
Write-Host "Waiting 3 seconds..." -ForegroundColor Cyan
Start-Sleep -Seconds 3

# Read config again to verify
$configVerify = Get-Content $configPath | ConvertFrom-Json
Write-Host "Verified WatchdogEnabled: $($configVerify.WatchdogEnabled)" -ForegroundColor $(if ($configVerify.WatchdogEnabled) { "Green" } else { "Red" })

if ($configVerify.WatchdogEnabled) {
    Write-Host ""
    Write-Host "SUCCESS! Config persisted." -ForegroundColor Green
    Write-Host "Now start the watchdog manually:" -ForegroundColor Yellow
    Write-Host "  Start-Process 'C:\ProgramData\RansomGuard\RGWorker.exe'" -ForegroundColor Gray
} else {
    Write-Host ""
    Write-Host "FAILED! Config was overwritten." -ForegroundColor Red
    Write-Host "Something is resetting the config file." -ForegroundColor Yellow
    
    # Restore backup
    Copy-Item $backupPath $configPath -Force
    Write-Host "Restored backup" -ForegroundColor Yellow
}

Write-Host ""
