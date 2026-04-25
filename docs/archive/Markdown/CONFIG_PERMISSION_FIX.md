# Configuration Permission Fix

> **Date:** April 25, 2026  
> **Issue:** Configuration changes not persisting  
> **Root Cause:** File permission issue  
> **Status:** 🔧 **SOLUTION IDENTIFIED**

---

## 🐛 Problem Summary

**Symptom:**
- User toggles "Self-healing Protection" ON in Settings
- UI appears to save the change
- But after restart, it's OFF again
- Config file shows `WatchdogEnabled: false`

**Root Cause:**
The config file `C:\ProgramData\RansomGuard\config.json` has restrictive permissions:

```
Owner: NT AUTHORITY\SYSTEM
Permissions:
  - SYSTEM → Full Control ✅
  - Administrators → Full Control ✅
  - Users → Read and Execute ONLY ❌ (NO WRITE!)
```

**Impact:**
- The **Service** (runs as SYSTEM) CAN write to config ✅
- The **UI** (runs as User) CANNOT write to config ❌
- `ConfigurationService.Save()` fails silently with `UnauthorizedAccessException`

---

## ✅ Solution

### Option 1: Fix File Permissions (Recommended)

Run this PowerShell command **as Administrator**:

```powershell
# Open PowerShell as Administrator, then run:
$configPath = "C:\ProgramData\RansomGuard\config.json"
$acl = Get-Acl $configPath
$rule = New-Object System.Security.AccessControl.FileSystemAccessRule("BUILTIN\Users", "Modify", "Allow")
$acl.SetAccessRule($rule)
Set-Acl $configPath $acl

Write-Host "Permissions fixed!" -ForegroundColor Green
```

**Verify the fix:**
```powershell
Get-Acl "C:\ProgramData\RansomGuard\config.json" | Select-Object -ExpandProperty Access | Format-Table
```

Expected output:
```
IdentityReference          FileSystemRights  AccessControlType
-----------------          ----------------  -----------------
NT AUTHORITY\SYSTEM        FullControl       Allow
BUILTIN\Administrators     FullControl       Allow
BUILTIN\Users              Modify            Allow  ← Should show Modify now!
```

### Option 2: Use the Fix Script

We created a script for you:

```powershell
# Right-click PowerShell → Run as Administrator
cd "F:\Github Projects\RansomGuard"
.\fix-config-permissions.ps1
```

### Option 3: Manual Permission Fix (GUI)

1. Open File Explorer
2. Navigate to: `C:\ProgramData\RansomGuard\`
3. Right-click `config.json` → Properties
4. Go to **Security** tab
5. Click **Edit**
6. Select **Users**
7. Check **Modify** permission
8. Click **OK** → **OK**

---

## 🔧 Code Fix: Better Error Handling

The `ConfigurationService.Save()` method currently catches exceptions silently. We should improve it:

**File:** `RansomGuard.Core/Services/ConfigurationService.cs`

**Current code:**
```csharp
catch (Exception ex)
{
    // Log error but don't crash the application
    Console.WriteLine($"[ConfigurationService] ERROR saving configuration: {ex.Message}");
    Console.WriteLine($"[ConfigurationService] Stack trace: {ex.StackTrace}");
    System.Diagnostics.Debug.WriteLine($"Failed to save configuration: {ex.Message}");
}
```

**Improved code:**
```csharp
catch (UnauthorizedAccessException ex)
{
    // Permission denied - show user-friendly error
    Console.WriteLine($"[ConfigurationService] ERROR: Permission denied writing to {configPath}");
    Console.WriteLine($"[ConfigurationService] The config file may be read-only or you lack write permissions.");
    Console.WriteLine($"[ConfigurationService] Run the app as Administrator or fix file permissions.");
    System.Diagnostics.Debug.WriteLine($"Permission denied saving configuration: {ex.Message}");
    
    // TODO: Show MessageBox to user
    // MessageBox.Show("Cannot save settings. Permission denied. Please run as Administrator.", 
    //                 "RansomGuard", MessageBoxButton.OK, MessageBoxImage.Error);
}
catch (IOException ex)
{
    // File locked or other IO error
    Console.WriteLine($"[ConfigurationService] ERROR: Cannot write to {configPath}");
    Console.WriteLine($"[ConfigurationService] The file may be locked by another process.");
    System.Diagnostics.Debug.WriteLine($"IO error saving configuration: {ex.Message}");
}
catch (Exception ex)
{
    // Other errors
    Console.WriteLine($"[ConfigurationService] ERROR saving configuration: {ex.Message}");
    Console.WriteLine($"[ConfigurationService] Stack trace: {ex.StackTrace}");
    System.Diagnostics.Debug.WriteLine($"Failed to save configuration: {ex.Message}");
}
```

---

## 🎯 Why This Happened

### During Installation/First Run:

1. **Service starts first** (runs as SYSTEM)
2. Service creates `C:\ProgramData\RansomGuard\` directory
3. Service creates `config.json` with default permissions
4. File inherits SYSTEM ownership and restrictive permissions
5. **UI starts** (runs as User)
6. UI tries to save config → Permission Denied!

### Proper Installation Should:

1. Create the directory with proper permissions during install
2. OR have the UI create the config file (not the Service)
3. OR explicitly set permissions when creating the file

---

## 🔍 How to Verify the Fix

### Step 1: Fix Permissions

Run the PowerShell command above as Administrator.

### Step 2: Test Config Save

```powershell
# Test if you can now write to the config
$configPath = "C:\ProgramData\RansomGuard\config.json"
$config = Get-Content $configPath | ConvertFrom-Json
$config.WatchdogEnabled = $true
$config | ConvertTo-Json -Depth 10 | Set-Content $configPath

# Verify it saved
$verify = Get-Content $configPath | ConvertFrom-Json
if ($verify.WatchdogEnabled) {
    Write-Host "SUCCESS! Config is now writable." -ForegroundColor Green
} else {
    Write-Host "FAILED! Still cannot write." -ForegroundColor Red
}
```

### Step 3: Test in UI

1. Open RansomGuard
2. Go to Settings
3. Toggle "Self-healing Protection" ON
4. Close the app
5. Reopen the app
6. Check if "Self-healing Protection" is still ON ✅

### Step 4: Verify Watchdog Starts

```powershell
# Check if watchdog is running
Get-Process -Name "MaintenanceWorker" -ErrorAction SilentlyContinue

# If running, you should see:
# ProcessName      : MaintenanceWorker
# Id               : <some PID>
```

---

## 📊 Diagnostic Results

From `diagnose-config.ps1`:

```
[1/6] Identifying configuration location...
  Running as traditional install
  Config path: C:\ProgramData\RansomGuard\config.json

[2/6] Checking config file...
  Config file exists
  File size: 894 bytes
  Last modified: 04/25/2026 01:14:33
  WatchdogEnabled: False  ← Problem!

[3/6] Testing write permissions...
  Write access OK  ← This was misleading - it tested directory, not file!

[5/6] Searching for duplicate config files...
  Found: C:\Users\slayer\AppData\Local\RansomGuard\config.json (old)
  Found: C:\ProgramData\RansomGuard\config.json (current)
  WARNING: Multiple config files found!
```

**Additional Issue:** There's an old config file in LocalAppData. You can delete it:

```powershell
Remove-Item "$env:LocalAppData\RansomGuard\config.json" -Force
```

---

## 🚀 Permanent Fix for Future Installs

### Update Service Installation

**File:** `RansomGuard.Service/ProjectInstaller.cs` (or install script)

Add code to set proper permissions when creating the config directory:

```csharp
private static void EnsureConfigDirectoryPermissions()
{
    string configDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        "RansomGuard"
    );
    
    if (!Directory.Exists(configDir))
    {
        Directory.CreateDirectory(configDir);
    }
    
    // Set permissions: Users get Modify rights
    var dirInfo = new DirectoryInfo(configDir);
    var security = dirInfo.GetAccessControl();
    
    var rule = new FileSystemAccessRule(
        "BUILTIN\\Users",
        FileSystemRights.Modify,
        InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
        PropagationFlags.None,
        AccessControlType.Allow
    );
    
    security.AddAccessRule(rule);
    dirInfo.SetAccessControl(security);
}
```

Call this during service installation.

### Alternative: Use LocalApplicationData for UI Config

Instead of sharing config between Service and UI, each could have its own:

- **Service:** `C:\ProgramData\RansomGuard\service_config.json` (SYSTEM only)
- **UI:** `C:\Users\<user>\AppData\Local\RansomGuard\config.json` (User writable)
- **Communication:** Use IPC to sync settings between them

---

## 📝 Summary

### Problem:
- Config file owned by SYSTEM with read-only permissions for Users
- UI cannot save settings
- Changes appear to work but don't persist

### Solution:
1. **Immediate:** Fix file permissions using PowerShell as Admin
2. **Long-term:** Update installer to set proper permissions
3. **Better:** Improve error handling to alert users

### Files to Update:
- `RansomGuard.Core/Services/ConfigurationService.cs` - Better error handling
- Service installer - Set proper permissions on directory creation
- `diagnose-config.ps1` - Test file permissions, not just directory

---

## ✅ Action Items

- [ ] Run `fix-config-permissions.ps1` as Administrator
- [ ] Delete old config: `$env:LocalAppData\RansomGuard\config.json`
- [ ] Test toggling settings in UI
- [ ] Verify watchdog starts automatically
- [ ] Update `ConfigurationService.Save()` with better error handling
- [ ] Update installer to set proper permissions
- [ ] Update `diagnose-config.ps1` to test file write, not just directory

---

**Last Updated:** April 25, 2026  
**Status:** 🔧 Solution identified, awaiting permission fix
