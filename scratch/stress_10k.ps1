$testDir = "D:\StressTest_10k"
New-Item -ItemType Directory -Force $testDir
Write-Host "Creating 10,000 files..."
$start = Get-Date
1..10000 | ForEach-Object { 
    $f = "file_$_.txt"
    $null = New-Item -ItemType File -Path (Join-Path $testDir $f) -Force
}
$end = Get-Date
$duration = ($end - $start).TotalSeconds
Write-Host "10,000 files created in $duration seconds."
