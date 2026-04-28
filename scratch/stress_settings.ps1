$configPath = "C:\ProgramData\RGCoreEssentials\config.json"
Write-Host "Rapidly toggling AutoQuarantine 20 times..."
1..20 | ForEach-Object { 
    $c = Get-Content $configPath | ConvertFrom-Json
    $c.AutoQuarantine = -not $c.AutoQuarantine
    $json = $c | ConvertTo-Json
    $json | Set-Content $configPath
    Start-Sleep -Milliseconds 50
}
Write-Host "Done. Finalizing state..."
Get-Process RGService, RGUI
