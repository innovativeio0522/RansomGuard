# Uninstall All RansomGuard Services
# Run this as Administrator

Write-Host "Stopping and removing RansomGuard services..." -ForegroundColor Yellow

# Stop and remove RGService service
Write-Host "`n1. Stopping RGService service..." -ForegroundColor Cyan
Stop-Service -Name "RGService" -Force -ErrorAction SilentlyContinue
Start-Sleep -Seconds 1

Write-Host "2. Removing RGService service..." -ForegroundColor Cyan
sc.exe delete RGService
Start-Sleep -Seconds 1

# Stop and remove RansomGuardSentinel service (orphaned from old MSIX)
Write-Host "`n3. Removing RansomGuardSentinel service (orphaned)..." -ForegroundColor Cyan
sc.exe delete RansomGuardSentinel
Start-Sleep -Seconds 1

Write-Host "`n✓ All services removed successfully!" -ForegroundColor Green
Write-Host "`nVerifying..." -ForegroundColor Yellow
Get-Service | Where-Object { $_.DisplayName -like "*Ransom*" -or $_.ServiceName -like "*RGService*" } | Select-Object ServiceName, DisplayName, Status

Write-Host "`nIf no services are listed above, cleanup is complete." -ForegroundColor Green

Read-Host "`nPress Enter to exit"
