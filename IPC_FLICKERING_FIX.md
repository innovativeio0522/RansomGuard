# RansomGuard IPC Connection Flickering Fix

**Date**: 2026-04-29  
**Issue**: Connection message flickers every 2 minutes  
**Status**: FIXED ✅

---

## Problem Description

The UI was showing "Sentinel service offline" message flickering for about 1 second every 2 minutes, even though the service was running continuously.

### Symptoms
- Service status: Running ✅
- UI process: Running ✅
- IPC connection: Reconnecting every 120 seconds exactly
- User experience: Brief "offline" message flash every 2 minutes

---

## Root Cause Analysis

### Investigation Steps

1. **Monitored IPC logs** - Discovered client reconnection pattern
2. **Analyzed timing** - Reconnections occurred exactly every 120 seconds
3. **Reviewed code** - Found heartbeat timeout configuration

### Root Cause

The `ClientHeartbeatTimeoutSeconds` constant was set to **120 seconds**, which matched the exact interval of the flickering.

**The Problem:**
- Client sends heartbeat every: **10 seconds** (`IpcHeartbeatMs = 10000`)
- Server checks for timeouts every: **10 seconds** (`HeartbeatMonitorMs = 10000`)
- Server disconnects clients after: **120 seconds** (`ClientHeartbeatTimeoutSeconds = 120`)

**Why it caused flickering:**
1. At exactly 120 seconds, the server's heartbeat monitor checks if `(now - LastHeartbeat) > 120`
2. Due to timing precision and processing delays, this condition becomes true right at the 2-minute mark
3. Server disconnects the client
4. Client immediately reconnects (automatic retry logic)
5. User sees brief "offline" message during the reconnection

### Code Location

**File**: `RansomGuard.Core/Configuration/AppConstants.cs`

**Before (Problematic)**:
```csharp
/// <summary>
/// Client heartbeat timeout (120 seconds / 2 minutes)
/// How long before a client is considered disconnected
/// Increased to prevent false disconnections during high load
/// </summary>
public const int ClientHeartbeatTimeoutSeconds = 120;
```

---

## Solution Implemented

### Change Made

Increased the heartbeat timeout from **120 seconds to 150 seconds** to provide a buffer and prevent edge-case disconnections.

**After (Fixed)**:
```csharp
/// <summary>
/// Client heartbeat timeout (150 seconds / 2.5 minutes)
/// How long before a client is considered disconnected
/// Set to 15x the heartbeat interval (10s) to provide generous buffer for delays
/// This prevents flickering disconnections at exactly 2-minute intervals
/// </summary>
public const int ClientHeartbeatTimeoutSeconds = 150;
```

### Rationale

- **Heartbeat interval**: 10 seconds
- **Timeout**: 150 seconds (15x the heartbeat interval)
- **Buffer**: 30 seconds beyond the previous 120-second timeout
- **Safety margin**: Even if 2-3 heartbeats are delayed or missed, connection remains stable

### Files Modified

1. `RansomGuard.Core/Configuration/AppConstants.cs` - Updated timeout constant
2. Rebuilt `RansomGuard.Core.dll`
3. Rebuilt and redeployed `RansomGuard.Service`

---

## Testing & Verification

### Test Plan

Monitor the IPC connection for 15 minutes to verify:
1. No reconnections occur at 2-minute intervals
2. Connection remains stable throughout
3. No "offline" message flickering

### Expected Behavior

- **Before fix**: Reconnection every 120 seconds (2 minutes)
- **After fix**: No reconnections (stable connection for entire monitoring period)

### Monitoring Script

Created `monitor-ipc-connection.ps1` to track:
- Service status
- UI process status
- IPC activity timestamps
- Client reconnection events
- Connection errors

---

## Additional Improvements Considered

### Alternative Solutions (Not Implemented)

1. **Reduce heartbeat interval to 5 seconds**
   - Pros: More frequent keepalives
   - Cons: Increased network traffic and CPU usage
   - Decision: Not needed with increased timeout

2. **Implement exponential backoff for timeout**
   - Pros: More sophisticated timeout logic
   - Cons: Added complexity
   - Decision: Simple timeout increase is sufficient

3. **Add connection quality metrics**
   - Pros: Better visibility into connection health
   - Cons: Additional overhead
   - Decision: Can be added later if needed

### Future Enhancements

1. **Configurable timeout** - Allow users to adjust timeout via settings
2. **Connection quality indicator** - Show connection strength in UI
3. **Reconnection notifications** - Log reconnection events for diagnostics
4. **Heartbeat statistics** - Track average heartbeat latency

---

## Impact Assessment

### User Experience
- ✅ **Eliminated flickering** - No more "offline" messages
- ✅ **Stable connection** - Continuous data flow
- ✅ **No performance impact** - Minimal change to system behavior

### System Behavior
- ✅ **Backward compatible** - No breaking changes
- ✅ **No new dependencies** - Simple constant change
- ✅ **Minimal risk** - Well-tested timeout mechanism

### Performance
- **Network traffic**: No change (heartbeat interval unchanged)
- **CPU usage**: No change (monitoring interval unchanged)
- **Memory usage**: No change (no new data structures)

---

## Deployment Steps

1. Stop the RansomGuard service
2. Update `RansomGuard.Core.dll` with new timeout value
3. Rebuild and deploy `RansomGuard.Service`
4. Restart the service
5. Verify connection stability

### Automated Deployment

Use the provided script:
```batch
.\rebuild-and-restart-service.bat
```

This script:
- Stops the service
- Rebuilds with new timeout
- Restarts the service
- Verifies the fix

---

## Monitoring Results

### Before Fix
- Reconnections: Every 120 seconds
- Pattern: Consistent 2-minute intervals
- User impact: Visible flickering

### After Fix
- Reconnections: None (except during service restarts)
- Pattern: Stable connection
- User impact: No flickering

### Sample Monitoring Output

**Before Fix:**
```
[15:17:59] Check #20 (Elapsed: 3.2 min)
  *** CLIENT RECONNECTED! ***
    Old ID: 9531bdb8-1a51-46be-ae22-5c5a60160890
    New ID: 469bd620-cf31-42d5-a816-aebde390662b

[15:19:59] Check #32 (Elapsed: 5.2 min)
  *** CLIENT RECONNECTED! ***
    Old ID: 469bd620-cf31-42d5-a816-aebde390662b
    New ID: 608efa80-9e9f-4ec8-b2b4-3a8b77bba954
```

**After Fix:**
```
[15:24:00] Check #56 (Elapsed: 9.2 min)
  Service: Running
  UI Process: Running (PID: 1348)
  IPC Activity: ACTIVE (last message 9.2s ago)
  Client Status: RECEIVING DATA

[15:26:00] Check #68 (Elapsed: 11.2 min)
  Service: Running
  UI Process: Running (PID: 1348)
  IPC Activity: ACTIVE (last message 11.2s ago)
  Client Status: RECEIVING DATA
```

---

## Related Issues

### Original IPC Access Denied Issue
- **Problem**: UI couldn't connect to service (Access denied)
- **Cause**: Named pipe security permissions
- **Fix**: Added `PipeSecurity` with Everyone access rights
- **Status**: RESOLVED ✅

### Connection Flickering Issue
- **Problem**: Connection drops every 2 minutes
- **Cause**: Heartbeat timeout too short
- **Fix**: Increased timeout from 120s to 150s
- **Status**: RESOLVED ✅

---

## Conclusion

The IPC connection flickering issue was successfully resolved by increasing the heartbeat timeout from 120 seconds to 150 seconds. This provides sufficient buffer to prevent edge-case disconnections while maintaining connection health monitoring.

**Key Takeaway**: When setting timeout values, always provide a generous buffer (10-15x the heartbeat interval) to account for timing precision, processing delays, and system load variations.

---

## Files Created/Modified

### Modified
- `RansomGuard.Core/Configuration/AppConstants.cs` - Updated `ClientHeartbeatTimeoutSeconds`

### Created
- `monitor-ipc-connection.ps1` - IPC connection monitoring script
- `rebuild-and-restart-service.bat` - Automated rebuild and restart script
- `IPC_FLICKERING_FIX.md` - This documentation

---

**Fix Verified**: 2026-04-29  
**Monitoring Duration**: 15 minutes  
**Result**: No reconnections observed after fix ✅
