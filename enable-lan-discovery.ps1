# Enable LAN Peer Discovery for RansomGuard
# This script enables the LAN Circuit Breaker feature and configures Windows Firewall

Write-Host "=== RansomGuard LAN Peer Discovery Setup ===" -ForegroundColor Cyan
Write-Host ""

# Check if running as administrator
$isAdmin = ([Security.Principal.WindowsPrincipal] [Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $isAdmin) {
    Write-Host "ERROR: This script requires administrator privileges!" -ForegroundColor Red
    Write-Host "Please right-click and select 'Run as Administrator'" -ForegroundColor Yellow
    pause
    exit 1
}

$configPath = "C:\ProgramData\RGCoreEssentials\config.json"

# Step 1: Enable LAN Circuit Breaker in configuration
Write-Host "[1/4] Enabling LAN Circuit Breaker..." -ForegroundColor Yellow
try {
    if (Test-Path $configPath) {
        $config = Get-Content $configPath | ConvertFrom-Json
        $config.LanCircuitBreakerEnabled = $true
        
        # Optional: Set a shared secret for secure peer authentication
        # Uncomment and set the same secret on all systems for secure mode
        # $config.LanSharedSecret = "YourSecretKeyHere"
        
        $config | ConvertTo-Json -Depth 10 | Set-Content $configPath
        Write-Host "   ✓ LAN Circuit Breaker enabled in config" -ForegroundColor Green
    } else {
        Write-Host "   ✗ Configuration file not found at $configPath" -ForegroundColor Red
        exit 1
    }
} catch {
    Write-Host "   ✗ Failed to update configuration: $_" -ForegroundColor Red
    exit 1
}

# Step 2: Configure Windows Firewall for UDP port 47700
Write-Host "[2/4] Configuring Windows Firewall..." -ForegroundColor Yellow
$port = 47700
$ruleName = "RansomGuard LAN Discovery"

try {
    # Remove existing rule if present
    $existingRule = Get-NetFirewallRule -DisplayName $ruleName -ErrorAction SilentlyContinue
    if ($existingRule) {
        Remove-NetFirewallRule -DisplayName $ruleName
        Write-Host "   ✓ Removed existing firewall rule" -ForegroundColor Green
    }
    
    # Create inbound rule for UDP
    New-NetFirewallRule -DisplayName $ruleName `
                        -Direction Inbound `
                        -Protocol UDP `
                        -LocalPort $port `
                        -Action Allow `
                        -Profile Private,Domain `
                        -Description "Allows RansomGuard to discover and communicate with peers on the local network" | Out-Null
    
    Write-Host "   ✓ Firewall rule created for UDP port $port (Inbound)" -ForegroundColor Green
    
    # Create outbound rule for UDP
    New-NetFirewallRule -DisplayName "$ruleName (Outbound)" `
                        -Direction Outbound `
                        -Protocol UDP `
                        -LocalPort $port `
                        -Action Allow `
                        -Profile Private,Domain `
                        -Description "Allows RansomGuard to broadcast discovery beacons on the local network" | Out-Null
    
    Write-Host "   ✓ Firewall rule created for UDP port $port (Outbound)" -ForegroundColor Green
} catch {
    Write-Host "   ✗ Failed to configure firewall: $_" -ForegroundColor Red
    Write-Host "   You may need to manually allow UDP port $port in Windows Firewall" -ForegroundColor Yellow
}

# Step 3: Restart the RansomGuard service
Write-Host "[3/4] Restarting RansomGuard service..." -ForegroundColor Yellow
try {
    $service = Get-Service -Name "RGService" -ErrorAction SilentlyContinue
    if ($service) {
        if ($service.Status -eq "Running") {
            Stop-Service -Name "RGService" -Force
            Write-Host "   ✓ Service stopped" -ForegroundColor Green
        }
        
        Start-Service -Name "RGService"
        Start-Sleep -Seconds 2
        
        $service = Get-Service -Name "RGService"
        if ($service.Status -eq "Running") {
            Write-Host "   ✓ Service restarted successfully" -ForegroundColor Green
        } else {
            Write-Host "   ✗ Service failed to start" -ForegroundColor Red
        }
    } else {
        Write-Host "   ✗ RGService not found" -ForegroundColor Red
    }
} catch {
    Write-Host "   ✗ Failed to restart service: $_" -ForegroundColor Red
}

# Step 4: Verify configuration
Write-Host "[4/4] Verifying configuration..." -ForegroundColor Yellow
Start-Sleep -Seconds 1

$config = Get-Content $configPath | ConvertFrom-Json
Write-Host "   LAN Circuit Breaker: $($config.LanCircuitBreakerEnabled)" -ForegroundColor $(if ($config.LanCircuitBreakerEnabled) { "Green" } else { "Red" })
Write-Host "   Broadcast Port: $($config.LanBroadcastPort)" -ForegroundColor Cyan
Write-Host "   Shared Secret: $(if ($config.LanSharedSecret) { '***configured***' } else { 'None (open mode)' })" -ForegroundColor Cyan

Write-Host ""
Write-Host "=== Setup Complete ===" -ForegroundColor Green
Write-Host ""
Write-Host "IMPORTANT NOTES:" -ForegroundColor Yellow
Write-Host "1. LAN discovery uses UDP broadcasts on port 47700" -ForegroundColor White
Write-Host "2. Both systems must be on the same network subnet" -ForegroundColor White
Write-Host "3. Peers will appear in the UI within 5-15 seconds" -ForegroundColor White
Write-Host "4. For secure mode, set the same LanSharedSecret on all systems" -ForegroundColor White
Write-Host ""
Write-Host "TROUBLESHOOTING:" -ForegroundColor Yellow
Write-Host "- Check service logs: C:\ProgramData\RGCoreEssentials\Logs\sentinel_engine.log" -ForegroundColor White
Write-Host "- Verify network profile is 'Private' or 'Domain' (not 'Public')" -ForegroundColor White
Write-Host "- Ensure WiFi router allows UDP broadcasts between clients" -ForegroundColor White
Write-Host "- Some routers have 'AP Isolation' enabled which blocks peer discovery" -ForegroundColor White
Write-Host ""
pause
