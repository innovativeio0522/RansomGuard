# RansomGuard Service Monitor Script
# Monitors RGService and RansomGuardSentinel for 15 minutes

$monitorDuration = 15 * 60  # 15 minutes in seconds
$checkInterval = 30         # Check every 30 seconds
$startTime = Get-Date
$endTime = $startTime.AddMinutes(15)

Write-Host "=== RansomGuard Service Monitor ===" -ForegroundColor Green
Write-Host "Start Time: $($startTime.ToString('yyyy-MM-dd HH:mm:ss'))" -ForegroundColor Yellow
Write-Host "End Time: $($endTime.ToString('yyyy-MM-dd HH:mm:ss'))" -ForegroundColor Yellow
Write-Host "Monitoring for 15 minutes, checking every 30 seconds..." -ForegroundColor Cyan
Write-Host ""

$checkCount = 0
$offlineEvents = @()

while ((Get-Date) -lt $endTime) {
    $checkCount++
    $currentTime = Get-Date
    $elapsed = ($currentTime - $startTime).TotalMinutes
    
    Write-Host "[$($currentTime.ToString('HH:mm:ss'))] Check #$checkCount (Elapsed: $([math]::Round($elapsed, 1)) min)" -ForegroundColor White
    
    # Check RGService
    try {
        $rgService = Get-Service -Name "RGService" -ErrorAction SilentlyContinue
        if ($rgService) {
            $status = $rgService.Status
            $color = if ($status -eq "Running") { "Green" } else { "Red" }
            Write-Host "  RGService: $status" -ForegroundColor $color
            
            if ($status -ne "Running") {
                $offlineEvent = @{
                    Time = $currentTime
                    Service = "RGService"
                    Status = $status
                    Check = $checkCount
                }
                $offlineEvents += $offlineEvent
                Write-Host "  ⚠️  RGService is OFFLINE!" -ForegroundColor Red
            }
        } else {
            Write-Host "  RGService: NOT FOUND" -ForegroundColor Red
            $offlineEvent = @{
                Time = $currentTime
                Service = "RGService"
                Status = "NOT FOUND"
                Check = $checkCount
            }
            $offlineEvents += $offlineEvent
        }
    } catch {
        Write-Host "  RGService: ERROR - $($_.Exception.Message)" -ForegroundColor Red
    }
    
    # Check for RansomGuardSentinel service
    try {
        $sentinelService = Get-Service -Name "RansomGuardSentinel" -ErrorAction SilentlyContinue
        if ($sentinelService) {
            $status = $sentinelService.Status
            $color = if ($status -eq "Running") { "Green" } else { "Red" }
            Write-Host "  RansomGuardSentinel: $status" -ForegroundColor $color
            
            if ($status -ne "Running") {
                $offlineEvent = @{
                    Time = $currentTime
                    Service = "RansomGuardSentinel"
                    Status = $status
                    Check = $checkCount
                }
                $offlineEvents += $offlineEvent
                Write-Host "  ⚠️  RansomGuardSentinel is OFFLINE!" -ForegroundColor Red
            }
        } else {
            Write-Host "  RansomGuardSentinel: NOT FOUND" -ForegroundColor Yellow
        }
    } catch {
        Write-Host "  RansomGuardSentinel: ERROR - $($_.Exception.Message)" -ForegroundColor Red
    }
    
    # Check for any RansomGuard processes
    $rgProcesses = Get-Process -Name "*RG*", "*RansomGuard*" -ErrorAction SilentlyContinue
    if ($rgProcesses) {
        Write-Host "  Active RG Processes:" -ForegroundColor Cyan
        foreach ($proc in $rgProcesses) {
            Write-Host "    - $($proc.ProcessName) (PID: $($proc.Id))" -ForegroundColor Gray
        }
    } else {
        Write-Host "  No RansomGuard processes found" -ForegroundColor Yellow
    }
    
    Write-Host ""
    
    # Wait for next check (unless it's the last iteration)
    if ((Get-Date).AddSeconds($checkInterval) -lt $endTime) {
        Start-Sleep -Seconds $checkInterval
    } else {
        break
    }
}

# Final summary
Write-Host "=== MONITORING COMPLETE ===" -ForegroundColor Green
Write-Host "Total Checks: $checkCount" -ForegroundColor White
Write-Host "Monitoring Duration: $([math]::Round(((Get-Date) - $startTime).TotalMinutes, 1)) minutes" -ForegroundColor White

if ($offlineEvents.Count -gt 0) {
    Write-Host ""
    Write-Host "⚠️  OFFLINE EVENTS DETECTED: $($offlineEvents.Count)" -ForegroundColor Red
    Write-Host ""
    foreach ($event in $offlineEvents) {
        Write-Host "  [$($event.Time.ToString('HH:mm:ss'))] Check #$($event.Check): $($event.Service) = $($event.Status)" -ForegroundColor Red
    }
} else {
    Write-Host ""
    Write-Host "✅ NO OFFLINE EVENTS - All services remained online during monitoring period" -ForegroundColor Green
}

Write-Host ""
Write-Host "Monitor completed at: $(Get-Date)" -ForegroundColor Yellow