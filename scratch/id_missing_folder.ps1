$json = Get-Content 'C:\ProgramData\RansomGuard\config.json' -Raw | ConvertFrom-Json
$paths = $json.MonitoredPaths
Write-Host "Total Paths in Config: $($paths.Count)"

$success = 0
foreach($p in $paths) {
    if (Test-Path $p) {
        try {
            $w = New-Object System.IO.FileSystemWatcher($p)
            $success++
            Write-Host "[SUCCESS] $p"
        } catch {
            Write-Host "[ERROR] $p : $($_.Exception.Message)"
        }
    } else {
        Write-Host "[MISSING] $p"
    }
}

Write-Host "Final Count: $success"
