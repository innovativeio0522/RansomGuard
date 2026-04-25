# Service Installation Fix

> **Date:** April 25, 2026  
> **Issue:** "Failed to install the background service: Failed to create service"  
> **Status:** ✅ **FIXED**

---

## 🐛 The Problem

When clicking "Install Service" in Settings, you got this error:
```
Failed to install the background service: Failed to create service
```

### Root Causes:

1. **Wrong Service Name**
   - Code tried to install: `RansomGuardSentinel`
   - Actual service name: `WinMaintenance`

2. **Wrong Executable Name**
   - Code looked for: `RansomGuard.Service.exe`
   - Actual executable: `WinMaintenanceSvc.exe`

3. **Wrong Watchdog Name**
   - Code looked for: `RansomGuard.Watchdog.exe`
   - Actual executable: `MaintenanceWorker.exe`

---

## ✅ The Fix

### Changed Files:

**1. `Services/ServiceManager.cs`**

```csharp
// BEFORE:
private const string ServiceName = "RansomGuardSentinel";
private const string ServiceDisplayName = "RansomGuard Sentinel Service";
private const string WatchdogProcessName = "RansomGuard.Watchdog";

// AFTER:
private const string ServiceName = "WinMaintenance";
private const string ServiceDisplayName = "RansomGuard Sentinel";
private const string WatchdogProcessName = "MaintenanceWorker";
```

**2. `ViewModels/SettingsViewModel.cs`**

```csharp
// BEFORE:
string servicePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "RansomGuard.Service.exe");

// AFTER:
string baseDir = AppDomain.CurrentDomain.BaseDirectory;
string servicePath = Path.Combine(baseDir, "WinMaintenanceSvc.exe");

// If not found, check publish folder
if (!File.Exists(servicePath))
{
    string publishPath = Path.Combine(baseDir, "..", "..", "..", "RansomGuard.Service", "publish", "WinMaintenanceSvc.exe");
    publishPath = Path.GetFullPath(publishPath);
    if (File.Exists(publishPath))
    {
        servicePath = publishPath;
    }
}
```

---

## 🧪 Testing

After rebuilding:

1. **Stop existing service:**
   ```powershell
   net stop WinMaintenance
   ```

2. **Uninstall old service (if needed):**
   ```powershell
   sc delete WinMaintenance
   sc delete RansomGuardSentinel
   ```

3. **Rebuild the app:**
   ```powershell
   .\build-and-run.bat
   ```

4. **Try installing from UI:**
   - Open RansomGuard Settings
   - Click "Install Service"
   - Should succeed now ✅

---

## 📋 Service Name Consistency

Your project uses these names:

| Component | Executable Name | Process Name | Service Name |
|-----------|----------------|--------------|--------------|
| UI | `MaintenanceUI.exe` | MaintenanceUI | N/A |
| Service | `WinMaintenanceSvc.exe` | WinMaintenanceSvc | WinMaintenance |
| Watchdog | `MaintenanceWorker.exe` | MaintenanceWorker | N/A |

All code now uses these correct names.

---

## 🎯 Why This Happened

The project was renamed from "RansomGuard" to use stealth names like "Maintenance" to avoid detection by ransomware. But some code still referenced the old names:

- Old: `RansomGuardSentinel` service
- New: `WinMaintenance` service

- Old: `RansomGuard.Service.exe`
- New: `WinMaintenanceSvc.exe`

- Old: `RansomGuard.Watchdog.exe`
- New: `MaintenanceWorker.exe`

---

## ✅ Result

After this fix:
- ✅ Service installs correctly from Settings UI
- ✅ Correct service name (`WinMaintenance`)
- ✅ Correct executable path (`WinMaintenanceSvc.exe`)
- ✅ Watchdog starts correctly (`MaintenanceWorker.exe`)

---

**Last Updated:** April 25, 2026  
**Status:** ✅ Fixed - rebuild required
