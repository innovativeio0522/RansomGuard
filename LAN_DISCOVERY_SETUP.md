# RansomGuard LAN Peer Discovery Setup Guide

## Current Status
✅ **LAN Circuit Breaker has been ENABLED in configuration**
- Configuration file: `C:\ProgramData\RGCoreEssentials\config.json`
- LanCircuitBreakerEnabled: `true`
- Broadcast Port: `47700` (UDP)
- Shared Secret: Not configured (open mode - all peers trusted)

## Next Steps

### Step 1: Configure Windows Firewall (REQUIRES ADMIN)
Run the provided batch script as Administrator:
```
Right-click on: enable-lan-and-restart.bat
Select: "Run as Administrator"
```

This will:
1. Confirm LAN Circuit Breaker is enabled
2. Add firewall rules for UDP port 47700
3. Restart the RansomGuard service
4. Verify the configuration

### Step 2: Verify Network Profile
LAN discovery only works on **Private** or **Domain** networks, not **Public** networks.

To check your network profile:
1. Open Settings → Network & Internet
2. Click on your WiFi/Ethernet connection
3. Under "Network profile type", ensure it's set to **Private**

If it's set to Public, change it to Private:
```powershell
# Run as Administrator
Get-NetConnectionProfile | Set-NetConnectionProfile -NetworkCategory Private
```

### Step 3: Enable on the Other System
The WiFi-connected system also needs LAN Circuit Breaker enabled:
1. Copy `enable-lan-and-restart.bat` to the other system
2. Run it as Administrator
3. Both systems should discover each other within 5-15 seconds

## How LAN Discovery Works

### Broadcasting
- Every 5 seconds, each node broadcasts a UDP beacon on port 47700
- Beacons contain: NodeId, NodeName, IP Address, App Version
- Uses UDP broadcast to `255.255.255.255`

### Peer Discovery
- When a beacon is received, the peer is added to the list
- Peers timeout after 15 seconds of no beacons
- Peer list is sent to the UI via IPC

### Circuit Break Signal
- When mass encryption is detected, a CIRCUIT_BREAK signal is broadcast
- All peers receive the signal and trigger critical response
- This creates a distributed defense network

## Troubleshooting

### Peers Not Showing Up

**1. Check Service Status**
```powershell
Get-Service -Name "RGService"
# Should show: Status = Running
```

**2. Check Logs**
```powershell
Get-Content "C:\ProgramData\RGCoreEssentials\Logs\sentinel_engine.log" -Tail 50
# Look for: "[LAN] Circuit Breaker STARTED on port 47700"
```

**3. Check Firewall Rules**
```powershell
Get-NetFirewallRule -DisplayName "*RansomGuard LAN*" | Select-Object DisplayName, Enabled, Action
# Should show 2 rules: Inbound and Outbound, both Enabled and Allow
```

**4. Check Network Profile**
```powershell
Get-NetConnectionProfile | Select-Object Name, NetworkCategory
# Should show: NetworkCategory = Private or DomainAuthenticated
# NOT: Public
```

**5. Test UDP Connectivity**
On System A:
```powershell
# Listen for UDP packets
$udp = New-Object System.Net.Sockets.UdpClient 47700
$udp.Client.ReceiveTimeout = 10000
$endpoint = New-Object System.Net.IPEndPoint([System.Net.IPAddress]::Any, 0)
$data = $udp.Receive([ref]$endpoint)
Write-Host "Received from: $($endpoint.Address)"
$udp.Close()
```

On System B:
```powershell
# Send UDP broadcast
$udp = New-Object System.Net.Sockets.UdpClient
$udp.EnableBroadcast = $true
$bytes = [System.Text.Encoding]::UTF8.GetBytes("TEST")
$udp.Send($bytes, $bytes.Length, [System.Net.IPEndPoint]::new([System.Net.IPAddress]::Broadcast, 47700))
$udp.Close()
```

### Common Issues

**Issue: "Access Denied" when configuring firewall**
- Solution: Run the script as Administrator (right-click → Run as Administrator)

**Issue: Service won't start**
- Check if service path is correct: `sc qc RGService`
- Check service logs: `C:\ProgramData\RGCoreEssentials\Logs\boot.log`

**Issue: Peers discovered but then disappear**
- Check if beacons are being sent every 5 seconds
- Peers timeout after 15 seconds of no beacons
- Check for network issues or packet loss

**Issue: WiFi router blocking broadcasts**
- Some routers have "AP Isolation" or "Client Isolation" enabled
- This prevents devices from communicating with each other
- Check router settings and disable AP Isolation
- Common in guest networks and public WiFi

**Issue: Different subnets**
- UDP broadcasts don't cross subnet boundaries
- Both systems must be on the same subnet (e.g., 192.168.1.x)
- Check IP addresses: `ipconfig`

## Security Considerations

### Open Mode (Current)
- No shared secret configured
- All LAN peers are trusted
- Suitable for home networks or trusted environments

### Secure Mode (Recommended for Enterprise)
To enable HMAC authentication:
1. Edit `C:\ProgramData\RGCoreEssentials\config.json`
2. Set `LanSharedSecret` to a strong password (same on all systems)
3. Restart the service

Example:
```json
{
  "LanCircuitBreakerEnabled": true,
  "LanSharedSecret": "MySecurePassword123!",
  "LanBroadcastPort": 47700
}
```

**Important:** All systems must use the SAME shared secret to communicate.

## Verification

Once everything is configured, you should see:
1. In the UI: "LAN Peers" section showing discovered systems
2. In logs: `[LAN] New peer discovered: COMPUTERNAME (192.168.x.x)`
3. Peer count updates in real-time

## Manual Firewall Configuration

If the batch script doesn't work, configure manually:

**Windows Defender Firewall:**
1. Open Windows Defender Firewall with Advanced Security
2. Click "Inbound Rules" → "New Rule"
3. Rule Type: Port
4. Protocol: UDP, Port: 47700
5. Action: Allow the connection
6. Profile: Private, Domain (NOT Public)
7. Name: RansomGuard LAN Discovery
8. Repeat for "Outbound Rules"

**Or use netsh:**
```cmd
netsh advfirewall firewall add rule name="RansomGuard LAN Discovery" dir=in action=allow protocol=UDP localport=47700 profile=private,domain
netsh advfirewall firewall add rule name="RansomGuard LAN Discovery (Outbound)" dir=out action=allow protocol=UDP localport=47700 profile=private,domain
```
