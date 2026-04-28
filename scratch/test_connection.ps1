$logPath = 'C:\ProgramData\RGCoreEssentials\Logs\ipc_client.log'
$initialCount = (Get-Content $logPath).Count
Write-Host "Starting High Load Connection Test..."
1..100 | ForEach-Object { 
    New-Item -ItemType File -Force "D:\Test Monitor\stress_$_.txt" | Out-Null
    if ($_ % 10 -eq 0) { New-Item -ItemType File -Force "D:\Test Monitor\stress_$_.ransom" | Out-Null }
}
Write-Host "Simulating 10 second idle period..."
Start-Sleep -Seconds 10
$finalLogs = Get-Content $logPath | Select-Object -Skip $initialCount
$disconnections = $finalLogs | Select-String -Pattern "Disconnected|offline|timed out"
if ($disconnections) {
    Write-Host "FAILURE: Disconnections detected!"
    $disconnections
} else {
    Write-Host "SUCCESS: No disconnections detected during high load."
}
