# Update RansomGuard Service with new DLLs
Write-Host "Updating RansomGuard Sentinel Service..." -ForegroundColor Yellow

# Set working directory to script location
$scriptPath = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location $scriptPath
Write-Host "Working directory: $scriptPath" -ForegroundColor Cyan

try {
    # Stop the service
    Write-Host "Stopping service..." -ForegroundColor Cyan
    Stop-Service -Name "RansomGuardSentinel" -Force
    Start-Sleep -Seconds 2
    
    # Copy updated DLLs
    Write-Host "Copying updated files..." -ForegroundColor Cyan
    $coreDll = Join-Path $scriptPath "RansomGuard.Core\bin\Debug\net8.0\RansomGuard.Core.dll"
    $serviceDll = Join-Path $scriptPath "RansomGuard.Service\bin\Debug\net8.0-windows\RansomGuard.Service.dll"
    $serviceExe = Join-Path $scriptPath "RansomGuard.Service\bin\Debug\net8.0-windows\RansomGuard.Service.exe"
    $publishDir = Join-Path $scriptPath "RansomGuard.Service\publish"
    
    Copy-Item $coreDll -Destination "$publishDir\RansomGuard.Core.dll" -Force
    Copy-Item $serviceDll -Destination "$publishDir\RansomGuard.Service.dll" -Force
    Copy-Item $serviceExe -Destination "$publishDir\RansomGuard.Service.exe" -Force
    
    Write-Host "Files copied successfully!" -ForegroundColor Green
    
    # Start the service
    Write-Host "Starting service..." -ForegroundColor Cyan
    Start-Service -Name "RansomGuardSentinel"
    Start-Sleep -Seconds 2
    
    Write-Host "`nService updated and restarted successfully!" -ForegroundColor Green
    Write-Host "The WPF app should automatically reconnect within 2-4 seconds." -ForegroundColor Green
} catch {
    Write-Host "`nError: $_" -ForegroundColor Red
    Write-Host "Please run this script as Administrator" -ForegroundColor Yellow
}

Read-Host "`nPress Enter to exit"
