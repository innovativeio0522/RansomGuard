# Connection Flickering Fix - Summary

## Issue Description
The RansomGuard UI was showing the Sentinel Service connection status flickering between "Online" and "Offline" repeatedly during runtime, not just at startup. This indicated a real IPC connection stability issue.

## Root Cause Analysis

### Server-Side Heartbeat Timeout Mechanism
Located in `RansomGuard.Service/Communication/NamedPipeServer.cs` (lines 100-105):

```csharp
private async Task HeartbeatMonitorLoop(CancellationToken token)
{
    while (!token.IsCancellationRequested)
    {
        var now = DateTime.Now;
        foreach (var client in _clients.Values)
        {
            if ((now - client.LastHeartbeat).TotalSeconds > AppConstants.Ipc.ClientHeartbeatTimeoutSeconds)
            {
                System.Diagnostics.Debug.WriteLine($"[IPC] Client {client.Id} timed out. Removing.");
                client.MessageQueue.CompleteAdding();
                try { client.Writer.Dispose(); } catch { }
                _clients.TryRemove(client.Id, out _);
            }
        }
        await Task.Delay(AppConstants.Timers.HeartbeatMonitorMs, token).ConfigureAwait(false);
    }
}
```

### The Problem
1. **Client sends heartbeat every 10 seconds** (`IpcHeartbeatMs = 10000`)
2. **Server timeout was 30 seconds** (`ClientHeartbeatTimeoutSeconds = 30`)
3. **During high load** (many file activities, telemetry requests, threat alerts):
   - Message queue gets busy
   - Heartbeat messages get delayed in the queue
   - Server doesn't receive heartbeat within 30 seconds
   - Server disposes the writer, closing the pipe
   - Client sees EOF and reconnects
   - **Result: Flickering cycle**

## Fix Implemented

### Changed in `RansomGuard.Core/Configuration/AppConstants.cs`

**Before:**
```csharp
public const int ClientHeartbeatTimeoutSeconds = 30;
```

**After:**
```csharp
/// <summary>
/// Client heartbeat timeout (120 seconds / 2 minutes)
/// How long before a client is considered disconnected
/// Increased to prevent false disconnections during high load
/// </summary>
public const int ClientHeartbeatTimeoutSeconds = 120;
```

### Why This Works
- **4x more tolerance**: Changed from 30 seconds to 120 seconds (2 minutes)
- **Handles high load**: Even if heartbeat is delayed by message queue congestion, the server won't prematurely disconnect
- **Still detects real disconnections**: 2 minutes is reasonable for detecting actual client crashes/disconnections
- **Maintains 10-second heartbeat**: Client still sends heartbeat every 10 seconds for responsiveness

## Build Information
- **Version**: 1.0.1.4
- **Bundle Location**: `RansomGuard.Package\AppPackages\RansomGuard.Package_1.0.1.4_Test\RansomGuard.Package_1.0.1.4_x64.msixbundle`
- **Includes Fixes**:
  - Files Per Hour counter fix
  - Suspicious extension detection fix
  - Process detection improvements
  - **Connection flickering fix (heartbeat timeout)**

## Installation Instructions

### Step 1: Uninstall Current Version
1. Open Windows Settings → Apps → Installed apps
2. Find "RansomGuard" in the list
3. Click the three dots → Uninstall
4. Confirm uninstallation

### Step 2: Install New Version
1. Navigate to: `RansomGuard.Package\AppPackages\RansomGuard.Package_1.0.1.4_Test\`
2. Double-click: `RansomGuard.Package_1.0.1.4_x64.msixbundle`
3. Click "Install" in the installer window
4. Wait for installation to complete

### Step 3: Launch and Test
1. Launch RansomGuard from Start Menu
2. Verify connection status shows "Sentinel Service Online"
3. **Monitor for extended period** (at least 30-60 minutes) to verify no flickering
4. Perform file operations to generate activity and load

## Testing Checklist

- [ ] Connection status shows "Online" on startup
- [ ] No flickering after 5 minutes of idle
- [ ] No flickering after 15 minutes of idle
- [ ] No flickering after 30 minutes of idle
- [ ] No flickering during high file activity (copy/move/rename multiple files)
- [ ] No flickering during threat detection (create .ransom files)
- [ ] Connection remains stable for 1+ hour of continuous operation

## If Flickering Persists

If the flickering continues after installing version 1.0.1.4, we may need to investigate:

1. **Remove timeout entirely**: Make the server more tolerant by not disconnecting clients based on heartbeat timeout
2. **Increase heartbeat frequency**: Send heartbeats more often (e.g., every 5 seconds instead of 10)
3. **Priority queue for heartbeats**: Ensure heartbeat messages bypass the regular message queue
4. **Separate heartbeat channel**: Use a dedicated pipe or mechanism for heartbeats

## Related Files
- `RansomGuard.Core/Configuration/AppConstants.cs` - Contains all timing constants
- `RansomGuard.Service/Communication/NamedPipeServer.cs` - Server-side IPC with heartbeat monitor
- `Services/ServicePipeClient.cs` - Client-side IPC with heartbeat sender

## Status
✅ **Fix Implemented**  
✅ **MSIX Bundle Built (v1.0.1.4)**  
⏳ **Awaiting User Testing**

---
*Last Updated: Context Transfer - Connection Flickering Fix*
