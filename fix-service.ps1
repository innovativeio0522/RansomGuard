# Fix RansomGuard Service Registration
# Run this as Administrator

Write-Host "Removing stale service registration..." -ForegroundColor Yellow
sc.exe delete RansomGuardSentinel

Write-Host "`nWaiting for service to be removed..." -ForegroundColor Yellow
Start-Sleep -Seconds 2

Write-Host "`nService removed. Now:" -ForegroundColor Green
Write-Host "1. Open RansomGuard from Start Menu" -ForegroundColor Cyan
Write-Host "2. Go to Settings page" -ForegroundColor Cyan
Write-Host "3. Click 'Install Service' button" -ForegroundColor Cyan
Write-Host "`nThis will register the service with the correct path for version 1.0.1.4" -ForegroundColor Green

Read-Host "`nPress Enter to exit"
