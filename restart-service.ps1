# Restart RansomGuard Service
Write-Host "Restarting RansomGuard Sentinel Service..." -ForegroundColor Yellow

try {
    Restart-Service -Name "RansomGuardSentinel" -Force
    Write-Host "Service restarted successfully!" -ForegroundColor Green
    Start-Sleep -Seconds 2
} catch {
    Write-Host "Error: $_" -ForegroundColor Red
    Write-Host "Please run this script as Administrator" -ForegroundColor Yellow
}

Read-Host "Press Enter to exit"
