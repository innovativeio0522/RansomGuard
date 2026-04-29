# Manual Service Fix Instructions

## The Problem
The RGService is configured with the wrong path and cannot start. You need administrator privileges to fix this.

## Quick Fix Steps

### Step 1: Open PowerShell as Administrator
1. Press `Win + X` on your keyboard
2. Select **"Windows PowerShell (Admin)"** or **"Terminal (Admin)"**
3. Click **"Yes"** when prompted by User Account Control

### Step 2: Navigate to Project Directory
```powershell
cd "F:\Github Projects\RansomGuard"
```

### Step 3: Run These Commands

Copy and paste each command one at a time:

```powershell
# Stop the service if running
Stop-Service -Name "RGService" -Force -ErrorAction SilentlyContinue

# Delete the old service
sc.exe delete RGService

# Wait 2 seconds
Start-Sleep -Seconds 2

# Set the correct path
$servicePath = "F:\Github Projects\RansomGuard\RansomGuard.Service\bin\Debug\net8.0-windows\win-x64\RGService.exe"

# Verify the file exists
if (Test-Path $servicePath) {
    Write-Host "Service executable found!" -ForegroundColor Green
    
    # Create the service with correct path
    sc.exe create RGService binPath= "`"$servicePath`"" start= auto DisplayName= "RansomGuard Sentinel"
    
    # Configure service to run as LocalSystem
    sc.exe config RGService obj= LocalSystem
    
    # Add description
    sc.exe description RGService "Real-time ransomware protection and file system monitoring service"
    
    Write-Host "Service installed successfully!" -ForegroundColor Green
    
    # Try to start the service
    Write-Host "Starting service..." -ForegroundColor Cyan
    Start-Service -Name "RGService"
    
    # Check status
    Get-Service -Name "RGService"
    
} else {
    Write-Host "ERROR: Service executable not found at: $servicePath" -ForegroundColor Red
    Write-Host "You may need to build the service first." -ForegroundColor Yellow
}
```

### Step 4: Verify Service is Running

```powershell
# Check service status
Get-Service -Name "RGService"

# Check service configuration
sc.exe qc RGService

# Check if service process is running
Get-Process -Name "RGService" -ErrorAction SilentlyContinue
```

Expected output:
```
Status   Name               DisplayName
------   ----               -----------
Running  RGService          RansomGuard Sentinel
```

---

## Alternative: Use the Batch File

If you prefer, you can also:

1. **Right-click** on `fix-service-simple.bat` in File Explorer
2. Select **"Run as administrator"**
3. Follow the prompts in the window that opens
4. Press any key when done

---

## If Service Won't Start

If the service installs but won't start, check Event Viewer:

1. Press `Win + X` → **Event Viewer**
2. Navigate to: **Windows Logs** → **Application**
3. Look for errors from source **"RGService"** or **".NET Runtime"**

Common issues:
- Missing dependencies (DLL files)
- Configuration file errors
- Permission issues with `C:\ProgramData\RGCoreEssentials\`
- .NET 8.0 Runtime not installed

---

## Create Required Directories

If the service fails due to missing directories:

```powershell
# Create data directories
New-Item -ItemType Directory -Path "C:\ProgramData\RGCoreEssentials" -Force
New-Item -ItemType Directory -Path "C:\ProgramData\RGCoreEssentials\Logs" -Force
New-Item -ItemType Directory -Path "C:\ProgramData\RGCoreEssentials\Quarantine" -Force

# Grant permissions to LocalSystem
icacls "C:\ProgramData\RGCoreEssentials" /grant "SYSTEM:(OI)(CI)F" /T
```

---

## Verify Everything Works

After the service is running:

```powershell
# Monitor service for 1 minute
$endTime = (Get-Date).AddMinutes(1)
while ((Get-Date) -lt $endTime) {
    $service = Get-Service -Name "RGService"
    $time = Get-Date -Format "HH:mm:ss"
    Write-Host "[$time] RGService: $($service.Status)" -ForegroundColor $(if ($service.Status -eq "Running") { "Green" } else { "Red" })
    Start-Sleep -Seconds 5
}
```

If the service stays "Running" for 1 minute, it's working correctly!

---

## Then Start the UI

Once the service is running:

```powershell
# Launch the UI application
Start-Process ".\bin\Debug\net8.0-windows\RGUI.exe"
```

The UI should connect to the service and display telemetry data.

---

## Need Help?

If you encounter errors:
1. Check Event Viewer (Windows Logs → Application)
2. Check service logs: `C:\ProgramData\RGCoreEssentials\Logs\`
3. Verify .NET 8.0 Runtime is installed: `dotnet --list-runtimes`

---

**Created**: 2026-04-29
