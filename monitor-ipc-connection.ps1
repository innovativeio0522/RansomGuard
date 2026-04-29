# RansomGuard IPC Connection Monitor
# Monitors the connection between service and UI for 15 minutes

$monitorDuration = 15 * 60  # 15 minutes in seconds
$checkInterval = 10         # Check every 10 seconds
$startTime = Get-Date
$endTime = $startTime.AddMinutes(15)

Write-Host "========================================" -ForegroundColor Green
Write-Host "RansomGuard IPC Connection Monitor" -ForegroundColor Green
Write-Host "========================================" -ForegroundColor Green
Write-Host "Start Time: $($startTime.ToString('yyyy-MM-dd HH:mm:ss'))" -ForegroundColor Yellow
Write-Host "End Time: $($endTime.ToString('yyyy-MM-dd HH:mm:ss'))" -ForegroundColor Yellow
Write-Host "Monitoring for 15 minutes, checking every 10 seconds..." -ForegroundColor Cyan
Write-Host ""

$checkCount = 0
$disconnectionEvents = @()
$lastClientId = $null
$lastMessageTime = $null
$connectionLostCount = 0
$reconnectionCount = 0

# Get initial state
$ipcLog = "C:\ProgramData\RGCoreEssentials\Logs\ipc.log"
$ipcClientLog = "C:\ProgramData\RGCoreEssentials\Logs\ipc_client.log"

while ((Get-Date) -lt $endTime) {
    $checkCount++
    $currentTime = Get-Date
    $elapsed = ($currentTime - $startTime).TotalMinutes
    
    Write-Host "[$($currentTime.ToString('HH:mm:ss'))] Check #$checkCount (Elapsed: $([math]::Round($elapsed, 1)) min)" -ForegroundColor White
    
    # Check service status
    try {
        $service = Get-Service -Name "RGService" -ErrorAction SilentlyContinue
        if ($service) {
            $serviceStatus = $service.Status
            $serviceColor = if ($serviceStatus -eq "Running") { "Green" } else { "Red" }
            Write-Host "  Service: $serviceStatus" -ForegroundColor $serviceColor
            
            if ($serviceStatus -ne "Running") {
                $disconnectionEvents += @{
                    Time = $currentTime
                    Type = "SERVICE_STOPPED"
                    Details = "Service is not running"
                    Check = $checkCount
                }
                Write-Host "  *** SERVICE STOPPED! ***" -ForegroundColor Red
            }
        } else {
            Write-Host "  Service: NOT FOUND" -ForegroundColor Red
            $disconnectionEvents += @{
                Time = $currentTime
                Type = "SERVICE_NOT_FOUND"
                Details = "Service does not exist"
                Check = $checkCount
            }
        }
    } catch {
        Write-Host "  Service: ERROR - $($_.Exception.Message)" -ForegroundColor Red
    }
    
    # Check UI process
    try {
        $uiProcess = Get-Process -Name "RGUI" -ErrorAction SilentlyContinue
        if ($uiProcess) {
            Write-Host "  UI Process: Running (PID: $($uiProcess.Id))" -ForegroundColor Green
        } else {
            Write-Host "  UI Process: NOT RUNNING" -ForegroundColor Red
            $disconnectionEvents += @{
                Time = $currentTime
                Type = "UI_CRASHED"
                Details = "UI process not found"
                Check = $checkCount
            }
        }
    } catch {
        Write-Host "  UI Process: ERROR" -ForegroundColor Red
    }
    
    # Check IPC server logs for client connections
    try {
        if (Test-Path $ipcLog) {
            $recentServerLogs = Get-Content $ipcLog -Tail 20 -ErrorAction SilentlyContinue
            
            # Look for client connection
            $connectionLines = $recentServerLogs | Where-Object { $_ -match "Client connected\. ID: ([a-f0-9\-]+)" }
            if ($connectionLines) {
                $latestConnection = $connectionLines | Select-Object -Last 1
                if ($latestConnection -match "ID: ([a-f0-9\-]+)") {
                    $currentClientId = $matches[1]
                    
                    if ($lastClientId -and $currentClientId -ne $lastClientId) {
                        Write-Host "  *** CLIENT RECONNECTED! ***" -ForegroundColor Yellow
                        Write-Host "    Old ID: $lastClientId" -ForegroundColor Gray
                        Write-Host "    New ID: $currentClientId" -ForegroundColor Gray
                        $reconnectionCount++
                        $disconnectionEvents += @{
                            Time = $currentTime
                            Type = "CLIENT_RECONNECTED"
                            Details = "Client ID changed from $lastClientId to $currentClientId"
                            Check = $checkCount
                        }
                    }
                    
                    $lastClientId = $currentClientId
                }
            }
            
            # Look for recent messages (heartbeats, telemetry)
            $recentMessages = $recentServerLogs | Where-Object { $_ -match "Received from" -or $_ -match "Handshake received" }
            if ($recentMessages) {
                $latestMessage = $recentMessages | Select-Object -Last 1
                if ($latestMessage -match "(\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2})") {
                    $messageTime = [DateTime]::ParseExact($matches[1], "yyyy-MM-dd HH:mm:ss", $null)
                    $timeSinceLastMessage = ($currentTime - $messageTime).TotalSeconds
                    
                    if ($timeSinceLastMessage -lt 30) {
                        Write-Host "  IPC Activity: ACTIVE (last message $([math]::Round($timeSinceLastMessage, 1))s ago)" -ForegroundColor Green
                    } else {
                        Write-Host "  IPC Activity: STALE (last message $([math]::Round($timeSinceLastMessage, 1))s ago)" -ForegroundColor Yellow
                        
                        if ($timeSinceLastMessage -gt 60) {
                            $disconnectionEvents += @{
                                Time = $currentTime
                                Type = "IPC_STALE"
                                Details = "No messages for $([math]::Round($timeSinceLastMessage, 1)) seconds"
                                Check = $checkCount
                            }
                        }
                    }
                }
            } else {
                Write-Host "  IPC Activity: NO RECENT MESSAGES" -ForegroundColor Yellow
            }
        }
    } catch {
        Write-Host "  IPC Logs: ERROR - $($_.Exception.Message)" -ForegroundColor Red
    }
    
    # Check IPC client logs for errors
    try {
        if (Test-Path $ipcClientLog) {
            $recentClientLogs = Get-Content $ipcClientLog -Tail 10 -ErrorAction SilentlyContinue
            
            # Look for connection errors
            $errorLines = $recentClientLogs | Where-Object { $_ -match "ERROR|EXCEPTION|Access to the path is denied" }
            if ($errorLines) {
                $latestError = $errorLines | Select-Object -Last 1
                if ($latestError -match "(\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2})") {
                    $errorTime = [DateTime]::ParseExact($matches[1], "yyyy-MM-dd HH:mm:ss", $null)
                    $timeSinceError = ($currentTime - $errorTime).TotalSeconds
                    
                    if ($timeSinceError -lt 30) {
                        Write-Host "  *** CLIENT ERROR DETECTED! ***" -ForegroundColor Red
                        Write-Host "    Error: $($latestError.Substring(0, [Math]::Min(100, $latestError.Length)))" -ForegroundColor Red
                        $connectionLostCount++
                        $disconnectionEvents += @{
                            Time = $currentTime
                            Type = "CLIENT_ERROR"
                            Details = "Recent error in client logs"
                            Check = $checkCount
                        }
                    }
                }
            }
            
            # Look for successful receives
            $receiveLines = $recentClientLogs | Where-Object { $_ -match "Received:" }
            if ($receiveLines) {
                Write-Host "  Client Status: RECEIVING DATA" -ForegroundColor Green
            } else {
                Write-Host "  Client Status: NO RECENT DATA" -ForegroundColor Yellow
            }
        }
    } catch {
        Write-Host "  Client Logs: ERROR - $($_.Exception.Message)" -ForegroundColor Red
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
Write-Host "========================================" -ForegroundColor Green
Write-Host "MONITORING COMPLETE" -ForegroundColor Green
Write-Host "========================================" -ForegroundColor Green
Write-Host "Total Checks: $checkCount" -ForegroundColor White
Write-Host "Monitoring Duration: $([math]::Round(((Get-Date) - $startTime).TotalMinutes, 1)) minutes" -ForegroundColor White
Write-Host "Connection Lost Events: $connectionLostCount" -ForegroundColor $(if ($connectionLostCount -eq 0) { "Green" } else { "Red" })
Write-Host "Reconnection Events: $reconnectionCount" -ForegroundColor $(if ($reconnectionCount -eq 0) { "Green" } else { "Yellow" })

if ($disconnectionEvents.Count -gt 0) {
    Write-Host ""
    Write-Host "*** ISSUES DETECTED: $($disconnectionEvents.Count) ***" -ForegroundColor Red
    Write-Host ""
    
    # Group by type
    $groupedEvents = $disconnectionEvents | Group-Object -Property Type
    foreach ($group in $groupedEvents) {
        Write-Host "  $($group.Name): $($group.Count) occurrence(s)" -ForegroundColor Yellow
    }
    
    Write-Host ""
    Write-Host "Detailed Event Log:" -ForegroundColor Yellow
    foreach ($event in $disconnectionEvents) {
        Write-Host "  [$($event.Time.ToString('HH:mm:ss'))] Check #$($event.Check): $($event.Type)" -ForegroundColor Red
        Write-Host "    Details: $($event.Details)" -ForegroundColor Gray
    }
} else {
    Write-Host ""
    Write-Host "*** NO ISSUES DETECTED ***" -ForegroundColor Green
    Write-Host "The IPC connection remained stable throughout the monitoring period." -ForegroundColor Green
}

Write-Host ""
Write-Host "Final Status Check:" -ForegroundColor Yellow
$finalService = Get-Service -Name "RGService" -ErrorAction SilentlyContinue
$finalUI = Get-Process -Name "RGUI" -ErrorAction SilentlyContinue

if ($finalService -and $finalService.Status -eq "Running") {
    Write-Host "  Service: RUNNING" -ForegroundColor Green
} else {
    Write-Host "  Service: NOT RUNNING" -ForegroundColor Red
}

if ($finalUI) {
    Write-Host "  UI: RUNNING (PID: $($finalUI.Id))" -ForegroundColor Green
} else {
    Write-Host "  UI: NOT RUNNING" -ForegroundColor Red
}

Write-Host ""
Write-Host "Monitor completed at: $(Get-Date)" -ForegroundColor Yellow
Write-Host ""
