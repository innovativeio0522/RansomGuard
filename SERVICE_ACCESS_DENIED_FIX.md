# RansomGuard Service "Access Denied" Error - Fix Guide

## Problem Summary

**Error**: "The RansomGuard Sentinel service failed to start due to the following error: Access is denied."

**Root Causes Identified**:
1. ✗ **Wrong Service Path**: Service is configured to run from a non-existent path
   - Configured: `F:\Github Projects\RansomGuard\bin\x64\Debug\net8.0-windows\win-x64\Service\RGService.exe`
   - Actual: `F:\Github Projects\RansomGuard\RansomGuard.Service\bin\Debug\net8.0-windows\win-x64\RGService.exe`

2. ⚠ **Administrator Privileges Required**: Windows services require admin rights to start/stop/install

3. ⚠ **UI Application Crash**: The RGUI.exe crashed with a stack overflow exception after ~2 minutes
   - Exception Code: `0xc00000fd` (Stack Overflow)
   - Faulting Module: `wpfgfx_cor3.dll` (WPF Graphics)

---

## Solution 1: Fix Service Path (RECOMMENDED)

### Option A: Using PowerShell Script (Easiest)

1. **Right-click PowerShell** and select **"Run as Administrator"**

2. Navigate to the project directory:
   ```powershell
   cd "F:\Github Projects\RansomGuard"
   ```

3. Run the fix script:
   ```powershell
   .\fix-service-path.ps1
   ```

This script will:
- Stop the existing service
- Remove the old service registration
- Find the correct service executable
- Reinstall the service with the correct path
- Attempt to start the service

### Option B: Manual Fix

1. **Open PowerShell as Administrator**

2. **Stop and remove the old service**:
   ```powershell
   Stop-Service -Name "RGService" -Force -ErrorAction SilentlyContinue
   sc.exe delete RGService
   ```

3. **Install the service with the correct path**:
   ```powershell
   $servicePath = "F:\Github Projects\RansomGuard\RansomGuard.Service\bin\Debug\net8.0-windows\win-x64\RGService.exe"
   sc.exe create RGService binPath= "`"$servicePath`"" start= auto DisplayName= "RansomGuard Sentinel"
   sc.exe config RGService obj= LocalSystem
   sc.exe description RGService "Real-time ransomware protection and file system monitoring service"
   ```

4. **Start the service**:
   ```powershell
   Start-Service -Name "RGService"
   ```

5. **Verify the service is running**:
   ```powershell
   Get-Service -Name "RGService"
   ```

---

## Solution 2: Using the Build Script

The `build-and-run.bat` script should handle service installation, but it requires administrator privileges.

1. **Right-click** `build-and-run.bat` and select **"Run as administrator"**

2. The script will:
   - Stop existing services
   - Build the projects
   - Publish the service
   - Start the service
   - Launch the UI

**Note**: The script has a self-elevation mechanism, but it may not work in all environments.

---

## Solution 3: Check Event Viewer for Details

If the service still won't start after fixing the path, check Windows Event Viewer for detailed error messages:

1. Press `Win + X` and select **"Event Viewer"**

2. Navigate to: **Windows Logs → Application**

3. Look for errors from source **"RGService"** or **".NET Runtime"**

Common issues found in Event Viewer:
- Missing dependencies (DLL files)
- Configuration file errors
- Permission issues with data directories
- .NET Runtime not installed

---

## Solution 4: Verify Service Dependencies

Ensure all required files are present:

```powershell
# Check if service executable exists
Test-Path "F:\Github Projects\RansomGuard\RansomGuard.Service\bin\Debug\net8.0-windows\win-x64\RGService.exe"

# Check for required DLLs
Get-ChildItem "F:\Github Projects\RansomGuard\RansomGuard.Service\bin\Debug\net8.0-windows\win-x64\" -Filter "*.dll"
```

If files are missing, rebuild the service:
```powershell
dotnet build RansomGuard.Service/RansomGuard.Service.csproj -c Debug
```

Or publish with all dependencies:
```powershell
dotnet publish RansomGuard.Service/RansomGuard.Service.csproj -c Debug -o RansomGuard.Service/publish --self-contained true -r win-x64
```

---

## Solution 5: Check Data Directory Permissions

The service needs write access to:
- `C:\ProgramData\RGCoreEssentials\`
- `C:\ProgramData\RGCoreEssentials\Logs\`
- `C:\ProgramData\RGCoreEssentials\Quarantine\`

Create directories and set permissions:
```powershell
# Create directories
New-Item -ItemType Directory -Path "C:\ProgramData\RGCoreEssentials" -Force
New-Item -ItemType Directory -Path "C:\ProgramData\RGCoreEssentials\Logs" -Force
New-Item -ItemType Directory -Path "C:\ProgramData\RGCoreEssentials\Quarantine" -Force

# Grant LocalSystem full control (service runs as LocalSystem)
icacls "C:\ProgramData\RGCoreEssentials" /grant "SYSTEM:(OI)(CI)F" /T
```

---

## UI Application Crash Issue

**Separate Issue**: The RGUI.exe application crashed with a stack overflow exception.

**Error Details**:
- Exception Code: `0xc00000fd` (Stack Overflow)
- Faulting Module: `wpfgfx_cor3.dll` (WPF Graphics Core)
- Time: Crashed after approximately 2 minutes of running

**Possible Causes**:
1. Infinite recursion in property change notifications
2. Circular dependency in data bindings
3. Infinite loop in value converters
4. Timer callback causing recursive updates

**Temporary Workaround**:
- Run the service independently without the UI
- Use Windows Services Manager to monitor service status
- Check logs in `C:\ProgramData\RGCoreEssentials\Logs\`

**Investigation Needed**:
- Review `DashboardViewModel.cs` timer callbacks
- Check for circular property dependencies
- Verify converter implementations don't cause recursion
- Test with service disconnected to isolate the issue

---

## Monitoring the Service

### Using PowerShell
```powershell
# Check service status
Get-Service -Name "RGService"

# Monitor service in real-time (check every 5 seconds)
while ($true) {
    $service = Get-Service -Name "RGService"
    $time = Get-Date -Format "HH:mm:ss"
    Write-Host "[$time] RGService: $($service.Status)" -ForegroundColor $(if ($service.Status -eq "Running") { "Green" } else { "Red" })
    Start-Sleep -Seconds 5
}
```

### Using Services Manager (GUI)
1. Press `Win + R`, type `services.msc`, press Enter
2. Find "RansomGuard Sentinel" in the list
3. Right-click → Properties to view details
4. Use Start/Stop/Restart buttons

### Using the Monitoring Script
```powershell
.\monitor-service.ps1
```
This will monitor the service for 15 minutes and report any offline events.

---

## Verification Steps

After applying the fix, verify everything is working:

1. **Check service status**:
   ```powershell
   Get-Service -Name "RGService" | Format-List *
   ```

2. **Check service path is correct**:
   ```powershell
   sc.exe qc RGService
   ```

3. **Check service is running**:
   ```powershell
   Get-Process -Name "RGService" -ErrorAction SilentlyContinue
   ```

4. **Check logs for errors**:
   ```powershell
   Get-Content "C:\ProgramData\RGCoreEssentials\Logs\sentinel_engine.log" -Tail 50
   ```

5. **Test IPC connection** (if UI is running):
   - Launch RGUI.exe
   - Check Dashboard for connection status
   - Verify telemetry is updating

---

## Summary of Issues Found

| Issue | Status | Solution |
|-------|--------|----------|
| Service path incorrect | ✗ Critical | Run `fix-service-path.ps1` as admin |
| Service not running | ✗ Critical | Start service after fixing path |
| UI crashes after 2 minutes | ✗ Critical | Needs code investigation |
| Access denied error | ✗ Critical | Requires admin privileges |
| Service executable missing | ✓ OK | Exists at correct location |

---

## Next Steps

1. **Immediate**: Fix the service path using the provided script
2. **Short-term**: Investigate and fix the UI crash issue
3. **Long-term**: Improve service installation process in build script

---

## Additional Resources

- **Event Viewer**: Check for detailed error messages
- **Service Logs**: `C:\ProgramData\RGCoreEssentials\Logs\`
- **Build Script**: `build-and-run.bat`
- **Fix Script**: `fix-service-path.ps1`
- **Monitor Script**: `monitor-service.ps1`

---

**Created**: 2026-04-29  
**Last Updated**: 2026-04-29
