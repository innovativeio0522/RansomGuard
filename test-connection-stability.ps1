# Test Connection Stability with 30-second timeout
# Monitors for 5 minutes to verify single connection fix

$testDuration = 5 * 60  # 5 minutes
$checkInterval = 5      # Check every 5 seconds
$startTime = Get-Date
$endTime = $startTime.AddMinutes(5)

Write-Host "========================================" -ForegroundColor Green
Write-Host "Connection Stability Test" -ForegroundColor Green
Write-Host "========================================" -ForegroundColor Green
Write-Host "Timeout: 30 seconds (3x heartbeat interval)" -ForegroundColor Yellow
Write-Host "Test Duration: 5 minutes" -ForegroundColor Yellow
Write-Host "Expected: NO disconnections if fix worked" -ForegroundColor Cyan
Write-Host ""

$checkCount = 0
$disconnections = @()
$lastClientId = $null

# Get initial client ID
$initialConnection = Get-Content "C:\ProgramData\RGCoreEssentials\Logs\ipc.log" -Tail 20 | 
    Select-String "Client connected\. ID: ([a-f0-9\-]+)" | 
    Select-Object -Last 1

if ($initialConnection -match "ID: ([a-f0-9\-]+)") {
    $lastClientId = $matches[1]
    Write-Host "Initial Client ID: $lastClientId" -ForegroundColor Cyan
    Write-Host ""
}

while ((Get-Date) -lt $endTime) {
    $checkCount++
    $currentTime = Get-Date
    $elapsed = ($currentTime - $startTime).TotalSeconds
    
    Write-Host "[$($currentTime.ToString('HH:mm:ss'))] Check #$checkCount (Elapsed: $([math]::Round($elapsed, 0))s)" -ForegroundColor White
    
    # Check for new connections (indicates reconnection)
    $recentConnections = Get-Content "C:\ProgramData\RGCoreEssentials\Logs\ipc.log" -Tail 10 | 
        Select-String "Client connected\. ID: ([a-f0-9\-]+)"
    
    if ($recentConnections) {
        $latestConnection = $recentConnections | Select-Object -Last 1
        if ($latestConnection -match "ID: ([a-f0-9\-]+)") {
            $currentClientId = $matches[1]
            
            if ($lastClientId -and $currentClientId -ne $lastClientId) {
                Write-Host "  *** RECONNECTION DETECTED! ***" -ForegroundColor Red
                Write-Host "    Old ID: $lastClientId" -ForegroundColor Gray
                Write-Host "    New ID: $currentClientId" -ForegroundColor Gray
                
                $disconnections += @{
                    Time = $currentTime
                    Elapsed = $elapsed
                    OldId = $lastClientId
                    NewId = $currentClientId
                }
                
                $lastClientId = $currentClientId
            }
        }
    }
    
    # Check for disconnection messages
    $recentDisconnects = Get-Content "C:\ProgramData\RGCoreEssentials\Logs\ipc.log" -Tail 5 | 
        Select-String "disconnected"
    
    if ($recentDisconnects) {
        $latestDisconnect = $recentDisconnects | Select-Object -Last 1
        if ($latestDisconnect -match "(\d{2}:\d{2}:\d{2})") {
            $disconnectTime = $matches[1]
            $timeDiff = ($currentTime - [DateTime]::ParseExact($disconnectTime, "HH:mm:ss", $null)).TotalSeconds
            
            if ($timeDiff -lt 10) {
                Write-Host "  Recent disconnection detected at $disconnectTime" -ForegroundColor Yellow
            }
        }
    }
    
    # Check heartbeat activity
    $recentHeartbeats = Get-Content "C:\ProgramData\RGCoreEssentials\Logs\ipc.log" -Tail 5 | 
        Select-String "Type.*11"
    
    if ($recentHeartbeats) {
        Write-Host "  Heartbeats: ACTIVE" -ForegroundColor Green
    } else {
        Write-Host "  Heartbeats: No recent activity" -ForegroundColor Yellow
    }
    
    # Check service status
    $service = Get-Service -Name "RGService" -ErrorAction SilentlyContinue
    if ($service -and $service.Status -eq "Running") {
        Write-Host "  Service: Running" -ForegroundColor Green
    } else {
        Write-Host "  Service: NOT RUNNING" -ForegroundColor Red
    }
    
    Write-Host ""
    
    if ((Get-Date).AddSeconds($checkInterval) -lt $endTime) {
        Start-Sleep -Seconds $checkInterval
    } else {
        break
    }
}

# Final summary
Write-Host "========================================" -ForegroundColor Green
Write-Host "TEST COMPLETE" -ForegroundColor Green
Write-Host "========================================" -ForegroundColor Green
Write-Host "Total Checks: $checkCount" -ForegroundColor White
Write-Host "Test Duration: $([math]::Round(((Get-Date) - $startTime).TotalMinutes, 1)) minutes" -ForegroundColor White
Write-Host "Disconnections: $($disconnections.Count)" -ForegroundColor $(if ($disconnections.Count -eq 0) { "Green" } else { "Red" })

if ($disconnections.Count -gt 0) {
    Write-Host ""
    Write-Host "*** DISCONNECTIONS DETECTED ***" -ForegroundColor Red
    Write-Host ""
    foreach ($disc in $disconnections) {
        Write-Host "  [$($disc.Time.ToString('HH:mm:ss'))] After $([math]::Round($disc.Elapsed, 0)) seconds" -ForegroundColor Red
        Write-Host "    $($disc.OldId) -> $($disc.NewId)" -ForegroundColor Gray
    }
    Write-Host ""
    Write-Host "RESULT: Single connection fix DID NOT resolve the issue" -ForegroundColor Red
} else {
    Write-Host ""
    Write-Host "*** NO DISCONNECTIONS ***" -ForegroundColor Green
    Write-Host ""
    Write-Host "RESULT: Single connection fix SUCCESSFULLY resolved the issue!" -ForegroundColor Green
    Write-Host "The connection remained stable for the entire test period." -ForegroundColor Green
    Write-Host "The duplicate connection was the root cause of the flickering." -ForegroundColor Green
}

Write-Host ""
Write-Host "Test completed at: $(Get-Date)" -ForegroundColor Yellow
