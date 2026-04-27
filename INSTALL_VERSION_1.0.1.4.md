# Install RansomGuard Version 1.0.1.4

## What's Fixed in This Version

✅ **Files Per Hour Counter** - Now correctly shows file activity rate  
✅ **Suspicious Extension Detection** - `.ransom`, `.locked`, `.encrypted` files now trigger alerts  
✅ **Process Detection Improvements** - Better identification of processes creating files  
✅ **Connection Flickering Fix** - IPC connection stability improved (heartbeat timeout increased from 30s to 120s)

---

## Installation Steps

### Step 1: Uninstall Current Version

1. Press `Win + I` to open Windows Settings
2. Go to **Apps** → **Installed apps**
3. Search for **"RansomGuard"** in the list
4. Click the **three dots (⋮)** next to RansomGuard
5. Click **Uninstall**
6. Confirm the uninstallation
7. Wait for the process to complete

### Step 2: Install New Version

1. Open File Explorer
2. Navigate to your RansomGuard project folder
3. Go to: `RansomGuard.Package\AppPackages\RansomGuard.Package_1.0.1.4_Test\`
4. **Double-click**: `RansomGuard.Package_1.0.1.4_x64.msixbundle`
5. In the installer window, click **Install**
6. Wait for installation to complete (should take 10-30 seconds)
7. Click **Launch** or find RansomGuard in the Start Menu

### Step 3: Verify Installation

After launching the app:

1. ✅ Check the **status bar** at the bottom shows: **"Sentinel Service Online"**
2. ✅ Go to **Dashboard** and verify:
   - CPU and RAM stats are updating
   - Files per hour counter is visible (small green label under "FOLDERS PROTECTED")
3. ✅ Open **Task Manager** (Ctrl+Shift+Esc) and verify these processes are running:
   - `RGUI.exe` (the UI)
   - `RGWorker.exe` (the watchdog)
   - `RGService.exe` (the service)

---

## Testing the Fixes

### Test 1: Files Per Hour Counter
1. Go to **Dashboard**
2. Create or modify a few files in your Documents folder
3. Look for the small green `+X / HOUR` label under the "FOLDERS PROTECTED" card
4. ✅ Counter should increment

### Test 2: Suspicious Extension Detection
1. Create a test folder (e.g., `C:\TestFolder`)
2. Add it to monitored paths in **Settings**
3. Create a file named `test.ransom` in that folder
4. ✅ An alert should appear on the Dashboard immediately

### Test 3: Connection Stability (Most Important!)
1. Leave the app running for **30-60 minutes**
2. Watch the status bar at the bottom
3. ✅ It should **always** show "Sentinel Service Online"
4. ✅ It should **NOT** flicker between "Online" and "Offline"

**During the 30-60 minute test:**
- Perform normal file operations (create, edit, delete files)
- Trigger some alerts by creating `.ransom` files
- Let it sit idle for periods

**If you see flickering:**
- Note the time it happened
- Check the logs at: `%LocalAppData%\RansomGuard\Logs\ipc_client.log`
- Report back with the log contents

---

## Troubleshooting

### Issue: "Sentinel Service Offline" on startup
**Solution:** Wait 5-10 seconds. The service takes a moment to start.

### Issue: Installation fails
**Solution:** 
1. Make sure the old version is fully uninstalled
2. Restart your computer
3. Try installing again

### Issue: App won't launch
**Solution:**
1. Check Task Manager - kill any lingering `RGUI.exe` processes
2. Try launching again from Start Menu

### Issue: Service not running
**Solution:**
1. Open PowerShell as Administrator
2. Run: `Get-Service -Name RGService`
3. If not running, go to Settings in the app and click "Install Service"

---

## Log Locations

If you need to check logs for troubleshooting:

- **IPC Client Log**: `%LocalAppData%\RansomGuard\Logs\ipc_client.log`
- **IPC Server Log**: `%LocalAppData%\RansomGuard\Logs\ipc.log`
- **Service Log**: `%LocalAppData%\RansomGuard\Logs\service.log`

To open the logs folder quickly:
1. Press `Win + R`
2. Type: `%LocalAppData%\RansomGuard\Logs`
3. Press Enter

---

## What to Report Back

After installing and testing for 30-60 minutes, please report:

1. ✅ or ❌ **Files per hour counter working?**
2. ✅ or ❌ **Suspicious extensions triggering alerts?**
3. ✅ or ❌ **Connection stable (no flickering)?**
4. ⏱️ **How long did you test?** (e.g., "tested for 45 minutes")
5. 📝 **Any issues observed?**

---

## Next Steps

Once you confirm the connection flickering is resolved, we can:
- Mark the connection stability tests as PASS in the manual test plan
- Move on to any remaining features or improvements
- Prepare for production release

---

*Installation guide for RansomGuard v1.0.1.4*
