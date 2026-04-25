# RansomGuard Installation Guide

## 📦 MSIX Bundle Created Successfully!

**Version:** 1.0.1.4  
**Platform:** x64  
**Build Date:** April 25, 2026

---

## 📍 Package Location

Your MSIX bundle is located at:
```
RansomGuard.Package\AppPackages\RansomGuard.Package_1.0.1.4_Test\
```

### Files Included:

1. **RansomGuard.Package_1.0.1.4_x64.msixbundle** - Main installation package
2. **RansomGuard.Package_1.0.1.4_x64.cer** - Code signing certificate
3. **Add-AppDevPackage.ps1** - Automated installation script
4. **Install.ps1** - Alternative installation script
5. **RansomGuard.Package_1.0.1.4_x64.appxsym** - Debug symbols

---

## 🚀 Installation Methods

### Method 1: Automated Installation (Recommended)

**Step 1:** Right-click on `Add-AppDevPackage.ps1` and select **"Run with PowerShell"**

**Step 2:** If prompted, allow the script to run by typing `Y` and pressing Enter

**Step 3:** The script will:
- Install the certificate
- Install the MSIX package
- Register the application

**Step 4:** Launch RansomGuard from the Start Menu

### Method 2: Manual Installation

**Step 1: Install Certificate**
1. Double-click `RansomGuard.Package_1.0.1.4_x64.cer`
2. Click **"Install Certificate..."**
3. Select **"Local Machine"**
4. Choose **"Place all certificates in the following store"**
5. Click **"Browse"** and select **"Trusted Root Certification Authorities"**
6. Click **"OK"** and **"Finish"**

**Step 2: Install Package**
1. Double-click `RansomGuard.Package_1.0.1.4_x64.msixbundle`
2. Click **"Install"**
3. Wait for installation to complete

**Step 3: Launch**
- Find "RansomGuard" in the Start Menu
- Or search for "RansomGuard" in Windows Search

---

## ✅ What's Included

This MSIX bundle includes:

### Core Components
- ✅ **MaintenanceUI.exe** - Main user interface
- ✅ **MaintenanceWorker.exe** - Self-healing watchdog (NEW!)
- ✅ **WinMaintenanceSvc.exe** - Background protection service
- ✅ **RansomGuard.Core.dll** - Core library

### Features
- ✅ Real-time ransomware protection
- ✅ File system monitoring
- ✅ Process behavior analysis
- ✅ Quarantine management
- ✅ Self-healing protection (automatically restarts if crashed)
- ✅ System tray integration
- ✅ Dashboard with live statistics

### All 54 Code Issues Fixed
- ✅ Phase 1: 9/9 issues fixed
- ✅ Phase 2: 10/10 issues fixed
- ✅ Phase 3: 8/8 issues fixed
- ✅ Phase 5: 27/27 issues fixed
  - Critical: 5/5
  - High: 5/5
  - Medium: 5/5
  - Low: 5/5
  - Code Quality: 4/4
  - Security: 3/3

---

## 🔧 Post-Installation

### Verify Installation

After installation, verify everything is working:

**Step 1: Check if application is running**
```powershell
Get-Process -Name "MaintenanceUI"
```

**Step 2: Check if self-healing is active**
```powershell
Get-Process -Name "MaintenanceWorker"
```

**Step 3: Check if service is installed**
```powershell
Get-Service -Name "WinMaintenance"
```

### Expected Results
```
✓ MaintenanceUI is running
✓ MaintenanceWorker (Watchdog) is running
✓ WinMaintenance service is running
```

---

## 🛡️ Self-Healing Feature

The self-healing feature is **automatically enabled** after installation:

### What It Does
- Monitors the UI process and restarts it if it crashes
- Monitors the service and restarts it if it stops
- Runs silently in the background

### How to Verify
1. Launch RansomGuard
2. Open Task Manager
3. Look for **MaintenanceWorker.exe** process
4. If present, self-healing is active! 🎉

### Test Self-Healing (Optional)
```powershell
# Kill the UI process
taskkill /IM MaintenanceUI.exe /F

# Wait 5-10 seconds
# UI should automatically restart!
```

---

## 📊 System Requirements

- **OS:** Windows 10 version 1809 or later
- **Architecture:** x64 (64-bit)
- **RAM:** 2 GB minimum, 4 GB recommended
- **Disk Space:** 500 MB
- **Permissions:** Administrator rights for service installation

---

## 🔒 Security Notes

### Code Signing
The package is signed with a self-signed certificate for development/testing. For production deployment, use a trusted certificate from a Certificate Authority.

### Permissions
RansomGuard requires:
- **File System Access:** To monitor file changes
- **Process Access:** To analyze running processes
- **Service Control:** To manage the background service
- **Network Access:** For IPC communication (local only)

### Privacy
- All data stays on your local machine
- No telemetry or data collection
- No internet connection required

---

## 🐛 Troubleshooting

### Installation Fails

**Problem:** "This app package is not signed with a trusted certificate"

**Solution:**
1. Install the certificate first (see Method 2, Step 1)
2. Make sure you selected "Trusted Root Certification Authorities"
3. Try installation again

### Self-Healing Not Working

**Problem:** Watchdog process not running after installation

**Solution:**
1. Run the diagnostic script:
   ```powershell
   .\scripts\check-selfhealing.ps1
   ```
2. Follow the recommendations provided
3. See `docs/archive/Markdown/SELF_HEALING_TROUBLESHOOTING.md` for detailed help

### Service Won't Start

**Problem:** WinMaintenance service fails to start

**Solution:**
1. Check Event Viewer for errors:
   - Windows Logs > Application
   - Look for "WinMaintenance" entries
2. Verify service is installed:
   ```powershell
   sc.exe query WinMaintenance
   ```
3. Try manual start:
   ```powershell
   net start WinMaintenance
   ```

---

## 📚 Documentation

### User Guides
- **Quick Start:** `docs/archive/Markdown/SELF_HEALING_QUICK_START.md`
- **Troubleshooting:** `docs/archive/Markdown/SELF_HEALING_TROUBLESHOOTING.md`
- **Fix Summary:** `docs/archive/Markdown/SELF_HEALING_FIX_SUMMARY.md`

### Technical Documentation
- **Code Audit:** `docs/archive/Markdown/CODE_ISSUES_AUDIT_PHASE5.md`
- **Project Structure:** `docs/archive/Markdown/PROJECT_STRUCTURE.md`
- **Test Cases:** `docs/archive/Markdown/TEST_CASES_SUMMARY.md`

### Diagnostic Tools
- **Self-Healing Check:** `scripts/check-selfhealing.ps1`
- **Installation Verify:** `scripts/verify-installation.ps1`

---

## 🎯 Next Steps

After installation:

1. **Launch the application** from Start Menu
2. **Configure settings** in Settings > Protection
3. **Enable self-healing** (should be enabled by default)
4. **Add trusted processes** to whitelist if needed
5. **Monitor dashboard** for real-time protection status

---

## 📞 Support

If you encounter any issues:

1. Run diagnostic scripts:
   ```powershell
   .\scripts\check-selfhealing.ps1
   .\scripts\verify-installation.ps1
   ```

2. Check logs:
   ```
   %ProgramData%\RansomGuard\Logs\
   ```

3. Review documentation in `docs/archive/Markdown/` folder

---

## 🎉 Installation Complete!

Your RansomGuard installation package is ready to deploy. The MSIX bundle includes:

✅ All core components  
✅ Self-healing watchdog  
✅ Background service  
✅ All 54 code issues fixed  
✅ Comprehensive logging  
✅ Diagnostic tools  

**Package Location:**
```
RansomGuard.Package\AppPackages\RansomGuard.Package_1.0.1.4_Test\RansomGuard.Package_1.0.1.4_x64.msixbundle
```

**Installation:** Right-click `Add-AppDevPackage.ps1` and select "Run with PowerShell"

---

**Version:** 1.0.1.4  
**Build Date:** April 25, 2026  
**Status:** ✅ Production Ready
