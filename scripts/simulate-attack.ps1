# RansomGuard Attack Simulator - Mass Encryption
$testFolder = "D:\Test Monitor"
$fileCount = 20
$extension = ".ransom"

if (-not (Test-Path $testFolder)) {
    New-Item -ItemType Directory -Path $testFolder -Force
}

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "RansomGuard Attack Simulator"
Write-Host "Target: $testFolder"
Write-Host "Files to Encrypt: $fileCount"
Write-Host "========================================" -ForegroundColor Cyan

# 1. Create dummy files
Write-Host "`n[1/2] Creating dummy files..." -ForegroundColor Yellow
for ($i = 1; $i -le $fileCount; $i++) {
    $filePath = Join-Path $testFolder "doc_$i.txt"
    "This is a sensitive document number $i. Do not delete." | Out-File -FilePath $filePath -Encoding utf8
}
Write-Host "Done. Created $fileCount files."

# 2. Perform Mass Encryption (Rapid Modification)
Write-Host "`n[2/2] STARTING MASS ENCRYPTION ATTACK..." -ForegroundColor Red
$startTime = Get-Date

foreach ($file in Get-ChildItem -Path $testFolder -Filter "*.txt") {
    $newPath = $file.FullName + $extension
    
    # Simulate encryption: Write random bytes and rename
    $randomBytes = [byte[]](1..100 | % { Get-Random -Minimum 0 -Maximum 255 })
    [System.IO.File]::WriteAllBytes($file.FullName, $randomBytes)
    
    Move-Item -Path $file.FullName -Destination $newPath -Force
    Write-Host "Encrypted: $($file.Name) -> $($file.Name)$extension"
}

$endTime = Get-Date
$duration = ($endTime - $startTime).TotalSeconds

Write-Host "`n========================================" -ForegroundColor Cyan
Write-Host "ATTACK COMPLETE!" -ForegroundColor Green
Write-Host "Duration: $duration seconds"
Write-Host "Files Modified: $fileCount"
Write-Host "Check RansomGuard Dashboard for alerts."
Write-Host "========================================" -ForegroundColor Cyan