# Automatic Firewall Configuration - Implementation Summary

## What Was Implemented

The application now **automatically configures Windows Firewall rules** when LAN Discovery is enabled. Users no longer need to manually run scripts.

## Changes Made

### 1. New FirewallManager Class
**File:** `RansomGuard.Core/Helpers/FirewallManager.cs`

Features:
- `EnsureLanFirewallRules()` - Creates firewall rules if they don't exist
- `RemoveLanFirewallRules()` - Removes firewall rules when disabled
- `CheckRuleExists()` - Verifies if rules are already configured
- `IsAdministrator()` - Checks if process has admin privileges

The class uses `netsh` commands to configure Windows Firewall:
- **Inbound Rule**: Allows UDP port 47700 (incoming peer discovery)
- **Outbound Rule**: Allows UDP port 47700 (outgoing broadcasts)
- **Profiles**: Private and Domain networks only (not Public)

### 2. Service Auto-Configuration
**File:** `RansomGuard.Service/Engine/LanCircuitBreaker.cs`

When the LAN Circuit Breaker starts:
1. Checks if LAN Discovery is enabled in config
2. **Automatically calls `FirewallManager.EnsureLanFirewallRules()`**
3. Logs success or failure
4. Continues starting even if firewall config fails (graceful degradation)

```csharp
// Automatically configure firewall rules
FileLogger.Log("sentinel_engine.log", "[LAN] Configuring firewall rules...");
bool firewallConfigured = Helpers.FirewallManager.EnsureLanFirewallRules();

if (!firewallConfigured)
{
    FileLogger.LogError("sentinel_engine.log", "[LAN] WARNING: Firewall rules could not be configured automatically...");
}
```

### 3. UI Settings Integration
**File:** `ViewModels/SettingsViewModel.cs`

When user enables LAN Circuit Breaker in Settings:
1. Attempts to configure firewall rules immediately
2. Shows a warning dialog if firewall config fails
3. Informs user they may need to run as Administrator

```csharp
partial void OnIsLanCircuitBreakerEnabledChanged(bool value)
{
    if (value)
    {
        // Ensure firewall rules are configured
        bool firewallConfigured = FirewallManager.EnsureLanFirewallRules();
        
        if (!firewallConfigured)
        {
            // Show warning to user
            MessageBox.Show("Firewall rules could not be configured automatically...");
        }
    }
}
```

## How It Works

### Scenario 1: Service Running as Administrator (Typical)
1. Service starts with admin privileges
2. LAN Circuit Breaker calls `EnsureLanFirewallRules()`
3. Firewall rules are created automatically
4. LAN discovery works immediately
5. ✅ **No user action required**

### Scenario 2: Service Running as User (Limited)
1. Service starts without admin privileges
2. LAN Circuit Breaker attempts firewall configuration
3. Configuration fails (access denied)
4. Warning logged to `sentinel_engine.log`
5. Service continues running (LAN discovery may not work)
6. ⚠️ **User needs to run as admin or manually configure firewall**

### Scenario 3: UI Enabling LAN Discovery
1. User toggles "LAN Circuit Breaker" in Settings
2. UI attempts to configure firewall
3. If successful: ✅ Rules created
4. If failed: ⚠️ Warning dialog shown
5. Service will retry when it restarts

## Firewall Rules Created

### Inbound Rule
```
Name: RansomGuard LAN Discovery
Direction: Inbound
Protocol: UDP
Port: 47700
Profiles: Private, Domain
Action: Allow
Description: Allows RansomGuard to discover and communicate with peers on the local network
```

### Outbound Rule
```
Name: RansomGuard LAN Discovery (Outbound)
Direction: Outbound
Protocol: UDP
Port: 47700
Profiles: Private, Domain
Action: Allow
Description: Allows RansomGuard to broadcast discovery beacons on the local network
```

## Logging

All firewall operations are logged to:
- `C:\ProgramData\RGCoreEssentials\Logs\firewall.log`
- `C:\ProgramData\RGCoreEssentials\Logs\sentinel_engine.log`

Example log entries:
```
[LAN] Configuring firewall rules...
[Firewall] LAN discovery rules created successfully.
[LAN] Firewall rules configured successfully.
[LAN] Circuit Breaker STARTED on port 47700.
```

## Benefits

### For Users
- ✅ **No manual script execution required**
- ✅ **Automatic configuration on first run**
- ✅ **Works out of the box** (when running as admin)
- ✅ **Clear warnings** if configuration fails
- ✅ **Graceful degradation** (app continues even if firewall fails)

### For Developers
- ✅ **Centralized firewall management**
- ✅ **Reusable `FirewallManager` class**
- ✅ **Proper error handling and logging**
- ✅ **No external dependencies** (uses built-in `netsh`)

## Testing

To verify automatic firewall configuration:

1. **Remove existing rules:**
   ```cmd
   netsh advfirewall firewall delete rule name="RansomGuard LAN Discovery"
   ```

2. **Start the service:**
   ```cmd
   net start RGService
   ```

3. **Check logs:**
   ```powershell
   Get-Content "C:\ProgramData\RGCoreEssentials\Logs\sentinel_engine.log" | Select-String "Firewall"
   ```

4. **Verify rules exist:**
   ```cmd
   netsh advfirewall firewall show rule name="RansomGuard LAN Discovery"
   ```

## Troubleshooting

### Issue: Firewall rules not created
**Cause:** Service not running with administrator privileges

**Solution:**
- Ensure service is installed and running as LocalSystem (default)
- Or manually run: `fix-lan-firewall.bat` as Administrator

### Issue: Warning dialog in UI
**Cause:** UI running without admin privileges

**Solution:**
- Click OK on the warning
- Service will configure firewall when it starts
- Or run UI as Administrator

### Issue: Rules created but LAN discovery not working
**Possible causes:**
1. Network profile is "Public" (change to "Private")
2. Different shared secrets on each system
3. Different subnets
4. WiFi router has AP Isolation enabled

## Future Enhancements

Potential improvements:
1. **UAC Elevation Prompt**: Automatically request elevation if needed
2. **Network Profile Check**: Warn if network is "Public"
3. **Rule Verification**: Periodically check if rules still exist
4. **Automatic Repair**: Recreate rules if they're deleted
5. **UI Indicator**: Show firewall status in Settings

## Backward Compatibility

The manual scripts still work:
- `fix-lan-firewall.bat` - Manual firewall configuration
- `start-with-lan-discovery.bat` - Full setup script
- `enable-lan-and-restart.bat` - Enable and restart

These are now **optional** and only needed if automatic configuration fails.

## Conclusion

✅ **Firewall configuration is now automatic!**

Users no longer need to:
- Run separate scripts
- Manually configure Windows Firewall
- Remember to enable firewall rules

The application handles everything automatically when LAN Discovery is enabled.
