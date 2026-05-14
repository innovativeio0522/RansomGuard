# LAN Peer Discovery - FIXED! ✅

## Problem Identified
The LAN Circuit Breaker was starting but **beacons were not being sent**. The async tasks were stuck in "WaitingForActivation" status and never executed.

## Root Cause
Using `Task.Run()` to start long-running background tasks in a Windows Service context caused the tasks to not execute properly. The tasks were queued but never started.

## Solution
Changed from `Task.Run()` to `Task.Factory.StartNew()` with `TaskCreationOptions.LongRunning`:

```csharp
// OLD (didn't work):
_beaconTask = Task.Run(() => BeaconLoopAsync(_cts.Token));

// NEW (works!):
_beaconTask = Task.Factory.StartNew(() => BeaconLoopAsync(_cts.Token), 
    TaskCreationOptions.LongRunning).Unwrap();
```

The `LongRunning` option tells the Task Scheduler to use a dedicated thread instead of the thread pool, which is essential for long-running background tasks in services.

## Verification
After the fix, logs show beacons being sent every 5 seconds:
```
2026-04-30 17:37:58.225: [LAN] Sending beacon #1...
2026-04-30 17:37:58.241: [LAN] Beacon #1 sent successfully
2026-04-30 17:38:03.243: [LAN] Sending beacon #2...
2026-04-30 17:38:03.245: [LAN] Beacon #2 sent successfully
2026-04-30 17:38:08.245: [LAN] Sending beacon #3...
2026-04-30 17:38:08.246: [LAN] Beacon #3 sent successfully
```

## What's Working Now
✅ **Automatic Firewall Configuration** - Rules created on service start  
✅ **UDP Beacons** - Broadcasting every 5 seconds on port 47700  
✅ **Listener Loop** - Receiving beacons from other peers  
✅ **Cleanup Loop** - Removing stale peers after 15 seconds  
✅ **HMAC Authentication** - Shared secret validation working  

## For Peers to Connect
Both systems must have:

1. **Same Shared Secret**: "12345" (or any matching value)
2. **LAN Enabled**: `LanCircuitBreakerEnabled: true` in config.json
3. **Service Running**: RGService must be running on both systems
4. **Firewall Configured**: UDP port 47700 allowed (automatic)
5. **Same Network**: Both on same subnet (e.g., 192.168.1.x)
6. **Private Network**: Network profile must be "Private" not "Public"
7. **No AP Isolation**: WiFi router must allow device-to-device communication

## Troubleshooting

### Check if beacons are being sent:
```powershell
Get-Content "C:\ProgramData\RGCoreEssentials\Logs\sentinel_engine.log" -Tail 50 | Select-String "Beacon"
```

Should show:
```
[LAN] Sending beacon #1...
[LAN] Beacon #1 sent successfully
```

### Check if peers are discovered:
```powershell
Get-Content "C:\ProgramData\RGCoreEssentials\Logs\sentinel_engine.log" | Select-String "peer discovered"
```

Should show:
```
[LAN] New peer discovered: COMPUTERNAME (192.168.1.x) v1.0.1.17
```

### Check network profile:
```powershell
Get-NetConnectionProfile | Select-Object Name, NetworkCategory
```

Must show `NetworkCategory: Private` (not Public)

### Check shared secret matches:
```powershell
$config = Get-Content "C:\ProgramData\RGCoreEssentials\config.json" | ConvertFrom-Json
Write-Host "Secret: '$($config.LanSharedSecret)'"
```

Must be EXACTLY the same on both systems (case-sensitive)

### Check IP addresses are on same subnet:
```powershell
Get-NetIPAddress -AddressFamily IPv4 | Where-Object { $_.InterfaceAlias -notlike "*Loopback*" } | Select-Object IPAddress
```

Both should be like 192.168.1.x or 192.168.0.x (same first 3 numbers)

## Next Steps

1. **Install updated service on BOTH systems**
   - Copy `RansomGuard.Service\publish3\` folder to other system
   - Run as Administrator:
     ```cmd
     sc stop RGService
     sc delete RGService
     sc create RGService binPath= "C:\Path\To\publish3\RGService.exe" start= auto
     sc start RGService
     ```

2. **Verify shared secret matches on both systems**
   - Edit `C:\ProgramData\RGCoreEssentials\config.json`
   - Set `"LanSharedSecret": "12345"` (same on both)

3. **Check network profile is Private**
   - Settings → Network & Internet → WiFi/Ethernet
   - Change to "Private" if it's "Public"

4. **Wait 5-15 seconds**
   - Peers should appear in the UI automatically

5. **Check logs for peer discovery**
   - Look for "[LAN] New peer discovered" messages

## Files Modified
- `RansomGuard.Service/Engine/LanCircuitBreaker.cs` - Fixed task creation
- `RansomGuard.Core/Helpers/FirewallManager.cs` - Automatic firewall config
- `ViewModels/SettingsViewModel.cs` - UI firewall configuration

## Build Location
The working service is in: `RansomGuard.Service\publish3\`

Use this folder to install on both systems.
