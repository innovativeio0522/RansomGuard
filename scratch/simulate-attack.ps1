# RansomGuard Attack Simulator
# Simulates rapid file creation/encryption activity to trigger the Velocity Shield.

$testDir = "D:\Test Monitor\AttackSimulation"
if (!(Test-Path $testDir)) { New-Item -ItemType Directory -Path $testDir }

Write-Host "--- Starting Simulated Attack in 3 seconds ---" -ForegroundColor Yellow
Start-Sleep -Seconds 3

Write-Host "!!! ATTACK STARTING !!!" -ForegroundColor Red
for ($i = 1; $i -le 40; $i++) {
    $filePath = Join-Path $testDir "attack_file_$i.locked"
    "This is a dummy file being 'encrypted' by a simulated ransomware." | Out-File -FilePath $filePath
    Write-Host "Encrypted: $filePath" -ForegroundColor DarkRed
    # No sleep = maximum velocity
}

Write-Host "--- Attack Finished ---" -ForegroundColor Green
Write-Host "Check RansomGuard Dashboard for 'MASSIVE FILE ENCRYPTION' alert." -ForegroundColor Cyan
