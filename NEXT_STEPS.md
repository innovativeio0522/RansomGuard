# ✅ LAN Discovery Configuration Complete

## Current Status

### ✅ What's Done
- **LAN Circuit Breaker: ENABLED** in configuration
- **UI Application: RUNNING** (PIDs: 2904, 6260)
- **Configuration file updated:** `C:\ProgramData\RGCoreEssentials\config.json`
- **Broadcast Port:** 47700 (UDP)
- **Authentication Mode:** Open (no shared secret required)

### ⚠️ What's Pending
- **Service: STOPPED** (needs administrator privileges to start)
- **Firewall: Not configured** (needs administrator privileges)

## 🚀 Next Steps to Enable LAN Discovery

### Option 1: Quick Start (Recommended)
**Run this command as Administrator:**
```
Right-click: start-with-lan-discovery.bat
Select: "Run as Administrator"
```

This single script will:
1. ✓ Configure Windows Firewall (UDP port 47700)
2. ✓ Build and install the service
3. ✓ Start the service with LAN discovery enabled
4. ✓ Launch the UI application
5. ✓ Show you the status

### Option 2: Manual Steps
If you prefer to do it manually:

**1. Configure Firewall (as Administrator):**
```cmd
netsh advfirewall firewall add rule name="RansomGuard LAN Discovery" dir=in action=allow protocol=UDP localport=47700 profile=private,domain
netsh advfirewall firewall add rule name="RansomGuard LAN Discovery (Outbound)" dir=out action=allow protocol=UDP localport=47700 profile=private,domain
```

**2. Start the Service (as Administrator):**
```cmd
net start RGService
```

**3. Check the UI:**
- The UI is already running
- Once service starts, it should connect automatically
- Look for "LAN Peers" section

## 📱 On the WiFi-Connected System

**Copy and run the same script:**
1. Copy `start-with-lan-discovery.bat` to the other computer
2. Right-click and "Run as Administrator"
3. Both systems should discover each other within 5-15 seconds!

## 🔍 How to Verify It's Working

### In the UI
Look for the **"LAN Peers"** section (usually in Settings or Dashboard):
- Should show discovered peers with:
  - Computer name
  - IP address
  - Status (Online/Offline)
  - App version

### In the Logs
Check: `C:\ProgramData\RGCoreEssentials\Logs\sentinel_engine.log`

You should see:
```
[LAN] Circuit Breaker STARTED on port 47700. NodeId=xxxxx, NodeName=COMPUTERNAME
[LAN] New peer discovered: OTHERNAME (192.168.x.x) v1.0.1.17
```

### Using PowerShell
```powershell
# Check service status
Get-Service -Name "RGService"

# Check firewall rules
Get-NetFirewallRule -DisplayName "*RansomGuard LAN*"

# Monitor logs in real-time
Get-Content "C:\ProgramData\RGCoreEssentials\Logs\sentinel_engine.log" -Wait -Tail 20
```

## 🐛 Troubleshooting

### Issue: "Access Denied" when starting service
**Solution:** Run the script as Administrator (right-click → Run as Administrator)

### Issue: Peers not showing up
**Check these in order:**

1. **Network Profile** (must be Private, not Public):
   ```powershell
   Get-NetConnectionProfile
   # If Public, change to Private:
   Get-NetConnectionProfile | Set-NetConnectionProfile -NetworkCategory Private
   ```

2. **Service Running:**
   ```powershell
   Get-Service -Name "RGService"
   # Should show: Status = Running
   ```

3. **Firewall Rules:**
   ```powershell
   Get-NetFirewallRule -DisplayName "*RansomGuard LAN*" | Select-Object DisplayName, Enabled
   # Should show 2 rules, both Enabled
   ```

4. **Same Subnet:**
   ```powershell
   ipconfig
   # Both systems should have IPs like: 192.168.1.x
   ```

5. **WiFi Router Settings:**
   - Check if "AP Isolation" or "Client Isolation" is enabled
   - This feature blocks devices from seeing each other
   - Disable it in router settings

### Issue: Service starts but no peers discovered
**Check logs:**
```powershell
Get-Content "C:\ProgramData\RGCoreEssentials\Logs\sentinel_engine.log" -Tail 50
```

Look for:
- `[LAN] Circuit Breaker STARTED` - Good!
- `[LAN] Circuit Breaker is DISABLED` - Bad! Config not loaded
- `[LAN] Failed to start Circuit Breaker` - Check port 47700 not in use

## 📊 Expected Behavior

### Beacon Broadcasting
- Every **5 seconds**, each system broadcasts a UDP beacon
- Beacon contains: NodeId, NodeName, IP Address, App Version
- Uses UDP broadcast to `255.255.255.255:47700`

### Peer Discovery
- When a beacon is received, peer is added to the list
- Peers timeout after **15 seconds** of no beacons
- Peer list updates are sent to UI via IPC

### Circuit Break Signal
- When mass encryption is detected, a **CIRCUIT_BREAK** signal is broadcast
- All peers receive it and trigger critical response
- Creates a distributed defense network across all LAN nodes

## 🔒 Security Notes

### Current Mode: Open
- No authentication required
- All peers on the same network are trusted
- Suitable for home networks

### Secure Mode (Optional)
To enable HMAC authentication:
1. Edit `C:\ProgramData\RGCoreEssentials\config.json`
2. Set `"LanSharedSecret": "YourSecretPassword123"`
3. Use the **SAME secret on all systems**
4. Restart service: `net stop RGService && net start RGService`

## 📞 Need More Help?

See detailed guides:
- **Quick Start:** `QUICK_START_LAN_DISCOVERY.md`
- **Full Guide:** `LAN_DISCOVERY_SETUP.md`
- **Service Logs:** `C:\ProgramData\RGCoreEssentials\Logs\`

## ✨ Summary

**What you need to do RIGHT NOW:**
1. Right-click `start-with-lan-discovery.bat`
2. Select "Run as Administrator"
3. Do the same on the WiFi system
4. Watch the peers appear in the UI! 🎉

That's it! The configuration is already done, you just need to start the service with admin privileges.
