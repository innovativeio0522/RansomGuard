$desktop = [System.Environment]::GetFolderPath('Desktop')
$lnk = Join-Path $desktop 'RansomGuard.lnk'

if (Test-Path $lnk) {
    Write-Host "Shortcut EXISTS on Desktop" -ForegroundColor Green
    $item = Get-Item $lnk
    Write-Host "  Name         : $($item.Name)"
    Write-Host "  Last Modified: $($item.LastWriteTime)"
} else {
    Write-Host "Shortcut NOT found on Desktop" -ForegroundColor Red
    Write-Host "Desktop path: $desktop"
    Write-Host ""
    Write-Host "All .lnk files on Desktop:" -ForegroundColor Yellow
    Get-ChildItem $desktop -Filter "*.lnk" | Select-Object Name
}
