# Quick Start: Enable LAN Peer Discovery

## What I've Done ✅
1. **Enabled LAN Circuit Breaker** in the configuration file
   - File: `C:\ProgramData\RGCoreEssentials\config.json`
   - Setting: `LanCircuitBreakerEnabled = true`

## What You Need to Do 🔧

### On This System (Current)
1. **Run the setup script as Administrator:**
   ```
   Right-click: enable-lan-and-restart.bat
   Select: "Run as Administrator"
   ```
   
   This will:
   - Configure Windows Firewall (UDP port 47700)
   - Restart the RansomGuard service
   - Verify everything is working

### On the WiFi System (Other Computer)
1. **Copy these files to the other system:**
   - `enable-lan-and-restart.bat`
   
2. **Run the script as Administrator** on that system too

3. **Both systems should discover each other within 5-15 seconds!**

## Verification
After running the script on both systems:
- Open RansomGuard UI on both computers
- Look for the "LAN Peers" section
- You should see the other computer listed with:
  - Computer name
  - IP address
  - Status: Online

## Troubleshooting

### If peers don't show up:

**Check 1: Network Profile**
Both systems must be on a **Private** network (not Public):
```powershell
Get-NetConnectionProfile
# If NetworkCategory = Public, change it:
Get-NetConnectionProfile | Set-NetConnectionProfile -NetworkCategory Private
```

**Check 2: Service Running**
```powershell
Get-Service -Name "RGService"
# Should show: Status = Running
```

**Check 3: Check Logs**
```
C:\ProgramData\RGCoreEssentials\Logs\sentinel_engine.log
```
Look for: `[LAN] Circuit Breaker STARTED on port 47700`

**Check 4: WiFi Router Settings**
- Some routers have "AP Isolation" or "Client Isolation" enabled
- This blocks devices from seeing each other
- Check your router settings and disable AP Isolation

**Check 5: Same Subnet**
Both systems must be on the same subnet:
```powershell
ipconfig
# Both should have IPs like: 192.168.1.x or 192.168.0.x
```

## How It Works
- Every 5 seconds, each system broadcasts a UDP beacon
- Other systems on the same network receive the beacon
- Peers are added to the list automatically
- If a peer stops sending beacons for 15 seconds, it's removed

## Security
- Currently in **open mode** (no authentication)
- All peers on the same network are trusted
- For secure mode, set the same `LanSharedSecret` in config.json on all systems

## Need Help?
See the detailed guide: `LAN_DISCOVERY_SETUP.md`
