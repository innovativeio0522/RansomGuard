# Diagnose LAN Peer Discovery Issues
Write-Host "=== RansomGuard LAN Discovery Diagnostics ===" -ForegroundColor Cyan
Write-Host ""

# 1. Check Configuration
Write-Host "[1] Configuration Check" -ForegroundColor Yellow
$config = Get-Content "C:\ProgramData\RGCoreEssentials\config.json" | ConvertFrom-Json
Write-Host "  LAN Enabled: $($config.LanCircuitBreakerEnabled)" -ForegroundColor $(if ($config.LanCircuitBreakerEnabled) { "Green" } else { "Red" })
Write-Host "  Port: $($config.LanBroadcastPort)" -ForegroundColor Cyan
Write-Host "  Shared Secret: $(if ($config.LanSharedSecret) { 'Set (' + $config.LanSharedSecret.Length + ' chars)' } else { 'Not set (open mode)' })" -ForegroundColor $(if ($config.LanSharedSecret) { "Green" } else { "Yellow" })
Write-Host ""

# 2. Check Service
Write-Host "[2] Service Check" -ForegroundColor Yellow
$service = Get-Service -Name "RGService" -ErrorAction SilentlyContinue
if ($service) {
    Write-Host "  Status: $($service.Status)" -ForegroundColor $(if ($service.Status -eq "Running") { "Green" } else { "Red" })
} else {
    Write-Host "  Status: Not installed" -ForegroundColor Red
}
Write-Host ""

# 3. Check Logs
Write-Host "[3] LAN Circuit Breaker Status" -ForegroundColor Yellow
$logPath = "C:\ProgramData\RGCoreEssentials\Logs\sentinel_engine.log"
if (Test-Path $logPath) {
    $lanLogs = Get-Content $logPath | Where-Object { $_ -match "\[LAN\]" } | Select-Object -Last 5
    if ($lanLogs) {
        $lanLogs | ForEach-Object { Write-Host "  $_" -ForegroundColor White }
    } else {
        Write-Host "  No LAN logs found" -ForegroundColor Red
    }
} else {
    Write-Host "  Log file not found" -ForegroundColor Red
}
Write-Host ""

# 4. Check Firewall
Write-Host "[4] Firewall Rules" -ForegroundColor Yellow
$rules = Get-NetFirewallRule -DisplayName "*RansomGuard LAN*" -ErrorAction SilentlyContinue
if ($rules) {
    $rules | ForEach-Object {
        $ruleDetails = Get-NetFirewallPortFilter -AssociatedNetFirewallRule $_ -ErrorAction SilentlyContinue
        Write-Host "  $($_.DisplayName): $($_.Direction) - $($_.Action) - Enabled: $($_.Enabled)" -ForegroundColor Green
        if ($ruleDetails) {
            Write-Host "    Port: $($ruleDetails.LocalPort), Protocol: $($ruleDetails.Protocol)" -ForegroundColor Gray
        }
    }
} else {
    Write-Host "  ⚠ NO FIREWALL RULES FOUND!" -ForegroundColor Red
    Write-Host "  Run as admin: netsh advfirewall firewall add rule name=`"RansomGuard LAN Discovery`" dir=in action=allow protocol=UDP localport=47700" -ForegroundColor Yellow
}
Write-Host ""

# 5. Check Network Profile
Write-Host "[5] Network Profile" -ForegroundColor Yellow
$profiles = Get-NetConnectionProfile
$profiles | ForEach-Object {
    $color = if ($_.NetworkCategory -eq "Private" -or $_.NetworkCategory -eq "DomainAuthenticated") { "Green" } else { "Red" }
    Write-Host "  $($_.Name): $($_.NetworkCategory)" -ForegroundColor $color
    if ($_.NetworkCategory -eq "Public") {
        Write-Host "    ⚠ Public networks block LAN discovery! Change to Private" -ForegroundColor Yellow
    }
}
Write-Host ""

# 6. Check IP Configuration
Write-Host "[6] IP Configuration" -ForegroundColor Yellow
$ips = Get-NetIPAddress -AddressFamily IPv4 | Where-Object { $_.InterfaceAlias -notlike "*Loopback*" }
$ips | ForEach-Object {
    Write-Host "  $($_.InterfaceAlias): $($_.IPAddress)/$($_.PrefixLength)" -ForegroundColor Cyan
}
Write-Host ""

# 7. Test UDP Port
Write-Host "[7] UDP Port Test" -ForegroundColor Yellow
try {
    $udp = New-Object System.Net.Sockets.UdpClient
    $udp.Client.SetSocketOption([System.Net.Sockets.SocketOptionLevel]::Socket, [System.Net.Sockets.SocketOptionName]::ReuseAddress, $true)
    $udp.Client.Bind([System.Net.IPEndPoint]::new([System.Net.IPAddress]::Any, 47700))
    Write-Host "  ⚠ Port 47700 is AVAILABLE (service may not be listening)" -ForegroundColor Yellow
    $udp.Close()
} catch {
    Write-Host "  ✓ Port 47700 is IN USE (service is listening)" -ForegroundColor Green
}
Write-Host ""

# 8. Listen for broadcasts
Write-Host "[8] Listening for UDP broadcasts (10 seconds)..." -ForegroundColor Yellow
Write-Host "  Press Ctrl+C to stop early" -ForegroundColor Gray
try {
    $udp = New-Object System.Net.Sockets.UdpClient 47701  # Use different port to avoid conflict
    $udp.Client.ReceiveTimeout = 10000
    $endpoint = New-Object System.Net.IPEndPoint([System.Net.IPAddress]::Any, 0)
    
    $startTime = Get-Date
    $packetsReceived = 0
    
    while (((Get-Date) - $startTime).TotalSeconds -lt 10) {
        try {
            $data = $udp.Receive([ref]$endpoint)
            $message = [System.Text.Encoding]::UTF8.GetString($data)
            Write-Host "  Received from $($endpoint.Address): $($message.Substring(0, [Math]::Min(50, $message.Length)))..." -ForegroundColor Green
            $packetsReceived++
        } catch [System.Net.Sockets.SocketException] {
            # Timeout, continue
        }
    }
    
    $udp.Close()
    
    if ($packetsReceived -eq 0) {
        Write-Host "  ⚠ No broadcasts received" -ForegroundColor Yellow
    } else {
        Write-Host "  ✓ Received $packetsReceived packet(s)" -ForegroundColor Green
    }
} catch {
    Write-Host "  Error: $_" -ForegroundColor Red
}
Write-Host ""

Write-Host "=== Summary ===" -ForegroundColor Cyan
Write-Host "Common issues:" -ForegroundColor Yellow
Write-Host "  1. Firewall not configured - blocks UDP port 47700" -ForegroundColor White
Write-Host "  2. Network profile is 'Public' - change to 'Private'" -ForegroundColor White
Write-Host "  3. Different shared secrets on each system" -ForegroundColor White
Write-Host "  4. Different subnets (e.g., 192.168.1.x vs 192.168.0.x)" -ForegroundColor White
Write-Host "  5. WiFi router has 'AP Isolation' enabled" -ForegroundColor White
Write-Host "  6. Other system's service not running or LAN disabled" -ForegroundColor White
Write-Host ""
