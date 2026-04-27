# RansomGuard Self-Healing Troubleshooting Guide

> **Date:** April 25, 2026  
> **Issue:** Self-healing (Watchdog) not working after installation  
> **Status:** 🔍 **DIAGNOSIS & SOLUTION**

---

## 📋 Problem Summary

The self-healing feature (Watchdog) is not working after installation. The Watchdog is responsible for:
- Monitoring the UI process (RGUI.exe) and restarting it if it crashes
- Monitoring the Service (WinMaintenance) and restarting it if it stops
- Running silently in the background

---

## 🔍 Root Cause Analysis

### Primary Issue: Missing Watchdog Executable

The most common reason self-healing doesn't work is that **RGWorker.exe** (the Watchdog executable) is not present in the installation directory.

**Why this happens:**
1. **Development builds** (`build-and-run.bat`) don't copy the Watchdog executable
2. **MSIX builds** (`build-msix.ps1`) properly copy the Watchdog, but only if the build completes successfully
3. **Manual installations** may not include all required executables

### Secondary Issues

1. **Self-healing disabled in settings** - User may have disabled it via Settings UI
2. **Service not installed** - Watchdog can't monitor a service that doesn't exist
3. **Insufficient permissions** - Watchdog needs proper permissions to start processes
4. **Path finding failures** - Watchdog can't find RGUI.exe to restart it

---

## 🛠️ Solution: Fix Build Scripts

### Option 1: Update build-and-run.bat (Recommended for Development)

The `build-and-run.bat` script needs to be updated to build and copy the Watchdog executable.

**Current behavior:**
- Only builds and runs the UI and Service
- Watchdog is never built or copied

**Required changes:**
1. Build the Watchdog project
2. Copy RGWorker.exe to the UI output directory
3. Ensure Watchdog starts with the UI

### Option 2: Use MSIX Build (Recommended for Production)

The `build-msix.ps1` script already handles Watchdog correctly:
```powershell
# Publishes Watchdog
dotnet publish RansomGuard.Watchdog\RansomGuard.Watchdog.csproj -c $Configuration -r win-$Platform --self-contained true -o $watchdogPublishDir

# Copies Watchdog to UI folder
Copy-Item -Path "$watchdogPublishDir\*" -Destination $uiPublishDir -Force

# Copies Watchdog to Service folder
Copy-Item -Path "$watchdogPublishDir\*" -Destination $servicePublishDir -Force
```

**To use:**
```powershell
.\build-msix.ps1 -Configuration Release -Platform x64
```

---

## 🔧 Manual Fix (Quick Workaround)

If you need self-healing to work immediately without rebuilding:

### Step 1: Build the Watchdog
```bash
cd RansomGuard.Watchdog
dotnet build -c Debug
```

### Step 2: Copy to UI Directory
```bash
# From project root
copy RansomGuard.Watchdog\bin\Debug\net8.0\RGWorker.exe bin\Debug\net8.0-windows\
```

### Step 3: Verify Files Exist
```bash
dir bin\Debug\net8.0-windows\RGWorker.exe
dir bin\Debug\net8.0-windows\RGUI.exe
```

### Step 4: Restart the Application
```bash
# Kill existing processes
taskkill /IM RGUI.exe /F
taskkill /IM RGWorker.exe /F

# Start the UI (it will launch the Watchdog)
start bin\Debug\net8.0-windows\RGUI.exe
```

---

## 📊 Diagnostic Checklist

Run the diagnostic script to identify the specific issue:
```powershell
.\check-selfhealing.ps1
```

### Expected Output (Working System)

```
=== RansomGuard Self-Healing Diagnostic ===

[1] Checking Watchdog Process...
    ✓ Watchdog is RUNNING (PID: 12345)

[2] Checking UI Process...
    ✓ UI is RUNNING (PID: 67890)

[3] Checking Service...
    ✓ Service is RUNNING

[4] Checking Configuration...
    ✓ Config file exists
    ✓ WatchdogEnabled = TRUE

[5] Checking Executable Files...
    ✓ Installation found: C:\Program Files\RansomGuard
    ✓ RGWorker.exe exists
    ✓ RGService.exe exists
```

### Common Failure Patterns

#### Pattern 1: Watchdog Not Running + Executable Missing
```
[1] Checking Watchdog Process...
    ✗ Watchdog is NOT RUNNING

[5] Checking Executable Files...
    ✓ Installation found: C:\...\bin\Debug\net8.0-windows
    ✗ RGWorker.exe NOT FOUND
    → This is the problem! Watchdog executable is missing.
```

**Solution:** Build and copy RGWorker.exe (see Manual Fix above)

#### Pattern 2: Watchdog Not Running + Disabled in Settings
```
[1] Checking Watchdog Process...
    ✗ Watchdog is NOT RUNNING

[4] Checking Configuration...
    ✓ Config file exists
    ✗ WatchdogEnabled = FALSE (Self-healing is DISABLED)
```

**Solution:** Enable self-healing in Settings > Self-healing Protection

#### Pattern 3: Service Not Installed
```
[3] Checking Service...
    ✗ Service NOT FOUND (not installed)
```

**Solution:** Install the service:
```bash
# Run as Administrator
sc.exe create RGService binPath= "C:\Path\To\RGService.exe" start= auto
sc.exe start RGService
```

---

## 🎯 Recommended Permanent Fix

### Update build-and-run.bat

Add Watchdog build and copy steps to the development build script:

```batch
echo [2/5] Building projects...
dotnet build RansomGuard.csproj -v q
dotnet build RansomGuard.Watchdog/RansomGuard.Watchdog.csproj -v q

echo [2.5/5] Copying Watchdog to UI directory...
copy /Y RansomGuard.Watchdog\bin\Debug\net8.0\RGWorker.exe bin\Debug\net8.0-windows\
```

This ensures the Watchdog is always available during development.

---

## 📝 Implementation Details

### How Self-Healing Works

1. **UI Startup** (`App.xaml.cs`):
   - Checks if `WatchdogEnabled = true` in config
   - Calls `WatchdogManager.EnsureProtectionEngaged()`
   - Launches MaintenanceWorker.exe if not already running

2. **Watchdog Process** (`RansomGuard.Watchdog/Program.cs`):
   - Runs in background (hidden console window)
   - Checks every 5 seconds:
     - Is MaintenanceUI.exe running? If not, restart it
     - Is WinMaintenance service running? If not, start it
   - Reads config every loop to check if user disabled it
   - Exits gracefully if `WatchdogEnabled = false`

3. **Path Finding** (`WatchdogManager.cs`):
   - Searches for MaintenanceWorker.exe in:
     1. Same directory as MaintenanceUI.exe (production)
     2. Parent directory (MSIX subfolder handling)
     3. Development paths (relative to project structure)
   - Logs all search attempts to `%ProgramData%\RansomGuard\Logs\ui_process.log`

### Logging Locations

All components write detailed logs for troubleshooting:

- **UI Process Log**: `%ProgramData%\RansomGuard\Logs\ui_process.log`
  - Watchdog launch attempts
  - Path search results
  - Service start attempts

- **Watchdog Log**: `%ProgramData%\RansomGuard\Logs\watchdog.log`
  - Process monitoring checks
  - UI restart attempts
  - Service restart attempts
  - Configuration changes

- **Service Log**: `%ProgramData%\RansomGuard\Logs\service.log`
  - Service startup/shutdown
  - IPC communication
  - Threat detection events

---

## 🔒 Security Considerations

### Why Watchdog Needs to Run

The Watchdog provides critical protection:
- **Ransomware resilience**: If ransomware kills the UI or Service, Watchdog restarts them
- **Crash recovery**: Automatic restart after unexpected crashes
- **Service monitoring**: Ensures the protection service is always running

### Permissions Required

- **UI Launch**: Normal user permissions (no elevation needed)
- **Service Control**: Requires admin rights to start/stop services
  - Watchdog uses `sc.exe` with UAC elevation prompt
  - Service is configured as auto-start, so manual intervention is rare

---

## ✅ Verification Steps

After applying the fix, verify self-healing works:

### Test 1: UI Crash Recovery
```bash
# Kill the UI process
taskkill /IM MaintenanceUI.exe /F

# Wait 5-10 seconds
# UI should automatically restart
```

### Test 2: Service Recovery
```bash
# Stop the service (requires admin)
net stop WinMaintenance

# Wait 5-10 seconds
# Service should automatically restart
```

### Test 3: Check Logs
```bash
# View Watchdog log
type "%ProgramData%\RansomGuard\Logs\watchdog.log"

# Should show monitoring activity every 5 seconds
```

---

## 📞 Support

If self-healing still doesn't work after following this guide:

1. Run the diagnostic script: `.\check-selfhealing.ps1`
2. Collect logs from `%ProgramData%\RansomGuard\Logs\`
3. Check Windows Event Viewer for application crashes
4. Verify all three executables exist:
   - MaintenanceUI.exe
   - MaintenanceWorker.exe
   - WinMaintenanceSvc.exe

---

## 📚 Related Files

- `Services/WatchdogManager.cs` - Watchdog lifecycle management
- `RansomGuard.Watchdog/Program.cs` - Watchdog monitoring logic
- `App.xaml.cs` - UI startup and Watchdog launch
- `check-selfhealing.ps1` - Diagnostic script
- `build-and-run.bat` - Development build script
- `build-msix.ps1` - Production MSIX build script

---

**Last Updated:** April 25, 2026  
**Status:** Solution documented, awaiting implementation
