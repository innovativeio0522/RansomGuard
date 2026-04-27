# Configuration Save Analysis

> **Date:** April 25, 2026  
> **Issue:** Configuration not persisting after changes  
> **Status:** 🔍 **INVESTIGATING**

---

## 📋 How Configuration is Saved

### 1. Configuration Storage Location

The config file location depends on whether the app is running as MSIX or traditional install:

**MSIX Package:**
```
%LocalAppData%\RansomGuard\config.json
Example: C:\Users\YourName\AppData\Local\RansomGuard\config.json
```

**Traditional Install:**
```
%ProgramData%\RansomGuard\config.json
Example: C:\ProgramData\RansomGuard\config.json
```

**Detection Logic:**
- Checks for `MSIX_PACKAGE_FAMILY_NAME` environment variable
- If present → Uses LocalApplicationData (user-specific, always writable)
- If absent → Uses ProgramData (system-wide, requires admin for writes)

### 2. Configuration Service Architecture

**Class:** `RansomGuard.Core/Services/ConfigurationService.cs`

**Pattern:** Thread-safe Singleton
```csharp
public static ConfigurationService Instance => _instance.Value;
```

**Key Properties:**
- `WatchdogEnabled` - Whether watchdog is active
- `MonitoredPaths` - List of folders to protect
- `SensitivityLevel` - Detection sensitivity (1-4)
- `RealTimeProtection` - Real-time scanning enabled
- `AutoQuarantine` - Auto-quarantine threats
- `NetworkIsolationEnabled` - Disconnect network on threat
- `EmergencyShutdownEnabled` - Shutdown PC on critical threat

### 3. Save Flow

#### When Settings are Changed in UI:

**File:** `ViewModels/SettingsViewModel.cs`

```csharp
// User toggles Watchdog in Settings
public bool IsWatchdogEnabled
{
    get => _isWatchdogEnabled;
    set
    {
        if (SetProperty(ref _isWatchdogEnabled, value))
        {
            // IMMEDIATE save to config
            ConfigurationService.Instance.WatchdogEnabled = value;
            ConfigurationService.Instance.Save();
            
            // Then start/stop watchdog
            if (value)
                WatchdogManager.EnsureProtectionEngaged();
            else
                WatchdogManager.KillWatchdog();
        }
    }
}
```

**Other settings use debounced save:**
```csharp
private void SaveConfig()
{
    if (!_isInitialized) return;
    _saveDebounceTimer.Stop();
    _saveDebounceTimer.Start(); // 500ms delay
}

private void SaveConfigImmediate()
{
    ConfigurationService.Instance.MonitoredPaths = MonitoredPaths.Select(m => m.Path).ToList();
    ConfigurationService.Instance.SensitivityLevel = SensitivityLevel;
    ConfigurationService.Instance.RealTimeProtection = IsRealTimeProtectionEnabled;
    ConfigurationService.Instance.AutoQuarantine = IsAutoQuarantineEnabled;
    ConfigurationService.Instance.WatchdogEnabled = IsWatchdogEnabled;
    ConfigurationService.Instance.NetworkIsolationEnabled = IsNetworkIsolationEnabled;
    ConfigurationService.Instance.EmergencyShutdownEnabled = IsEmergencyShutdownEnabled;
    ConfigurationService.Instance.Save(); // ← Actual save
}
```

#### The Save() Method:

**File:** `RansomGuard.Core/Services/ConfigurationService.cs`

```csharp
public void Save()
{
    if (IsTestingMode) return; // Skip in tests
    
    lock (_saveLock) // Thread-safe
    {
        try
        {
            var configPath = ConfigFile;
            var directory = Path.GetDirectoryName(configPath);
            
            // Ensure directory exists
            if (directory != null && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
                Console.WriteLine($"[ConfigurationService] Created config directory: {directory}");
            }
            
            // Serialize to JSON
            var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
            
            Console.WriteLine($"[ConfigurationService] Saving configuration to: {configPath}");
            Console.WriteLine($"[ConfigurationService] Config size: {json.Length} bytes");
            
            // Write to file
            File.WriteAllText(configPath, json);
            
            // Verify the file was written
            if (File.Exists(configPath))
            {
                var fileInfo = new FileInfo(configPath);
                Console.WriteLine($"[ConfigurationService] Configuration saved successfully. File size: {fileInfo.Length} bytes");
            }
            else
            {
                Console.WriteLine($"[ConfigurationService] WARNING: Config file does not exist after write!");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ConfigurationService] ERROR saving configuration: {ex.Message}");
            Console.WriteLine($"[ConfigurationService] Stack trace: {ex.StackTrace}");
        }
    }
    
    // Notify subscribers
    NotifyPathsChanged();
}
```

### 4. Configuration File Watcher

The service watches for external changes to the config file:

```csharp
private static FileSystemWatcher? _configWatcher;
private static System.Timers.Timer? _debounceTimer;

private static void StartWatcher()
{
    lock (_watcherLock)
    {
        var path = ConfigFile;
        var directory = Path.GetDirectoryName(path);
        
        _configWatcher = new FileSystemWatcher(directory, Path.GetFileName(path));
        _configWatcher.NotifyFilter = NotifyFilters.LastWrite;
        _configWatcher.Changed += (s, e) => {
            _debounceTimer?.Stop();
            _debounceTimer?.Start(); // 250ms debounce
        };
        
        _debounceTimer = new System.Timers.Timer(250);
        _debounceTimer.AutoReset = false;
        _debounceTimer.Elapsed += (s, e) => {
            ReloadInstance(); // Reload from disk
        };
        
        _configWatcher.EnableRaisingEvents = true;
    }
}
```

**Purpose:** If the Service or Watchdog modifies the config, the UI will reload it automatically.

---

## 🐛 Potential Issues

### Issue 1: MSIX vs Traditional Path Confusion

**Symptom:** Config saves to one location but reads from another

**Check:**
```powershell
# Check if running as MSIX
$env:MSIX_PACKAGE_FAMILY_NAME

# If MSIX, config should be at:
Get-Content "$env:LocalAppData\RansomGuard\config.json"

# If traditional, config should be at:
Get-Content "$env:ProgramData\RansomGuard\config.json"
```

**Solution:** Ensure you're checking the correct location based on install type.

### Issue 2: Permission Denied (Traditional Install)

**Symptom:** Save() throws UnauthorizedAccessException

**Cause:** ProgramData requires admin rights for writes

**Check:**
```powershell
# Test write access
$testFile = "$env:ProgramData\RansomGuard\test.txt"
try {
    "test" | Out-File $testFile
    Remove-Item $testFile
    Write-Host "✅ Write access OK"
} catch {
    Write-Host "❌ No write access: $($_.Exception.Message)"
}
```

**Solution:** 
- Run app as Administrator
- OR use MSIX package (uses LocalAppData which is always writable)

### Issue 3: File Watcher Conflict

**Symptom:** Config gets overwritten immediately after save

**Cause:** FileSystemWatcher detects the save and triggers a reload, potentially loading old data

**Check logs:**
```powershell
# Check for rapid save/reload cycles
Get-Content "$env:ProgramData\RansomGuard\Logs\*.log" | Select-String "Saving configuration|remote reload"
```

**Solution:** The debounce timer (250ms) should prevent this, but if multiple processes are running, they might conflict.

### Issue 4: Multiple Processes Fighting Over Config

**Symptom:** Config changes revert after a few seconds

**Cause:** 
- UI saves config with WatchdogEnabled = true
- Watchdog reads config, sees WatchdogEnabled = false (old cached value)
- Watchdog exits
- Service reads config, sees WatchdogEnabled = false
- Service saves config with WatchdogEnabled = false
- UI reloads config via FileWatcher, sees WatchdogEnabled = false

**Check:**
```powershell
# Check how many processes are running
Get-Process | Where-Object { 
    $_.ProcessName -like "*Maintenance*" -or 
    $_.ProcessName -like "*RansomGuard*" 
} | Select-Object ProcessName, Id, StartTime
```

**Solution:** Ensure only one instance of each process is running.

### Issue 5: Config File Locked by Another Process

**Symptom:** Save() succeeds but file content doesn't change

**Cause:** Another process has the file open with exclusive access

**Check:**
```powershell
# Check if file is locked (requires Handle.exe from Sysinternals)
handle.exe "$env:ProgramData\RansomGuard\config.json"
```

**Solution:** 
- Close all RansomGuard processes
- Delete config.json
- Restart app to create fresh config

### Issue 6: Antivirus/Security Software Blocking Writes

**Symptom:** Save() throws IOException or file doesn't update

**Cause:** Antivirus is blocking writes to ProgramData

**Check Windows Event Viewer:**
```powershell
Get-WinEvent -LogName "Microsoft-Windows-Windows Defender/Operational" -MaxEvents 50 | 
    Where-Object { $_.Message -like "*RansomGuard*" }
```

**Solution:** Add RansomGuard folder to antivirus exclusions.

---

## 🔍 Diagnostic Steps

### Step 1: Identify Config Location

```powershell
# Check if MSIX
if ($env:MSIX_PACKAGE_FAMILY_NAME) {
    Write-Host "Running as MSIX"
    $configPath = "$env:LocalAppData\RansomGuard\config.json"
} else {
    Write-Host "Running as traditional install"
    $configPath = "$env:ProgramData\RansomGuard\config.json"
}

Write-Host "Config path: $configPath"
```

### Step 2: Check Config File Exists and is Writable

```powershell
if (Test-Path $configPath) {
    Write-Host "✅ Config file exists"
    
    # Check file size
    $size = (Get-Item $configPath).Length
    Write-Host "File size: $size bytes"
    
    # Check last modified time
    $lastWrite = (Get-Item $configPath).LastWriteTime
    Write-Host "Last modified: $lastWrite"
    
    # Try to read it
    try {
        $config = Get-Content $configPath | ConvertFrom-Json
        Write-Host "✅ Config is valid JSON"
        Write-Host "WatchdogEnabled: $($config.WatchdogEnabled)"
    } catch {
        Write-Host "❌ Config is corrupted: $($_.Exception.Message)"
    }
} else {
    Write-Host "❌ Config file does not exist"
}
```

### Step 3: Monitor Config Changes in Real-Time

```powershell
# Watch for changes to config file
$watcher = New-Object System.IO.FileSystemWatcher
$watcher.Path = Split-Path $configPath
$watcher.Filter = Split-Path $configPath -Leaf
$watcher.NotifyFilter = [System.IO.NotifyFilters]::LastWrite

Register-ObjectEvent $watcher "Changed" -Action {
    $time = Get-Date -Format "HH:mm:ss.fff"
    Write-Host "[$time] Config file changed!"
    
    Start-Sleep -Milliseconds 100 # Wait for write to complete
    
    try {
        $config = Get-Content $configPath | ConvertFrom-Json
        Write-Host "  WatchdogEnabled: $($config.WatchdogEnabled)"
    } catch {
        Write-Host "  ERROR reading config: $($_.Exception.Message)"
    }
}

$watcher.EnableRaisingEvents = $true
Write-Host "Watching for changes to: $configPath"
Write-Host "Press Ctrl+C to stop..."

# Keep script running
while ($true) { Start-Sleep -Seconds 1 }
```

### Step 4: Check Application Logs

```powershell
# UI logs
Get-Content "$env:ProgramData\RansomGuard\Logs\ui_process.log" -Tail 50

# Watchdog logs
Get-Content "$env:ProgramData\RansomGuard\Logs\watchdog.log" -Tail 50

# Service logs (if available)
Get-Content "$env:ProgramData\RansomGuard\Logs\service.log" -Tail 50
```

### Step 5: Test Manual Config Edit

```powershell
# Backup current config
Copy-Item $configPath "$configPath.backup"

# Edit config manually
$config = Get-Content $configPath | ConvertFrom-Json
$config.WatchdogEnabled = $true
$config | ConvertTo-Json -Depth 10 | Set-Content $configPath

Write-Host "Manually set WatchdogEnabled = true"
Write-Host "Wait 5 seconds and check if it persists..."
Start-Sleep -Seconds 5

$config = Get-Content $configPath | ConvertFrom-Json
if ($config.WatchdogEnabled -eq $true) {
    Write-Host "✅ Config persisted"
} else {
    Write-Host "❌ Config was overwritten!"
}
```

---

## 🔧 Troubleshooting Solutions

### Solution 1: Force Config Rebuild

```powershell
# Stop all processes
Get-Process | Where-Object { 
    $_.ProcessName -like "*Maintenance*" -or 
    $_.ProcessName -like "*RansomGuard*" 
} | Stop-Process -Force

# Delete config
Remove-Item $configPath -Force

# Restart app - it will create fresh config with defaults
```

### Solution 2: Check for Duplicate Configs

```powershell
# Search for all config.json files
Get-ChildItem -Path C:\ -Filter "config.json" -Recurse -ErrorAction SilentlyContinue | 
    Where-Object { $_.FullName -like "*RansomGuard*" } |
    Select-Object FullName, Length, LastWriteTime
```

### Solution 3: Enable Verbose Logging

Add this to the beginning of `ConfigurationService.Save()`:

```csharp
Console.WriteLine($"[ConfigurationService] Save() called from: {Environment.StackTrace}");
```

This will show you exactly when and from where Save() is being called.

### Solution 4: Disable File Watcher Temporarily

In `ConfigurationService.Load()`, comment out:

```csharp
// if (!config.IsTestingMode)
// {
//     StartWatcher();
// }
```

This will prevent automatic reloads from external changes.

---

## 📊 Expected Behavior

### When User Enables Watchdog in Settings:

1. **UI:** User toggles "Self-healing Protection" ON
2. **SettingsViewModel:** `IsWatchdogEnabled` setter is called
3. **ConfigurationService:** `WatchdogEnabled` property is set to `true`
4. **ConfigurationService:** `Save()` is called immediately
5. **File System:** `config.json` is written with `"WatchdogEnabled": true`
6. **WatchdogManager:** `EnsureProtectionEngaged()` is called
7. **Watchdog:** MaintenanceWorker.exe is started
8. **Watchdog:** Reads config, sees `WatchdogEnabled = true`, continues running

### Config File Should Look Like:

```json
{
  "IsTestingMode": false,
  "HasAutoPopulated": true,
  "MonitoredPaths": [
    "c:\\users\\yourname\\documents",
    "c:\\users\\yourname\\desktop",
    "c:\\users\\yourname\\pictures",
    "c:\\users\\yourname\\music",
    "c:\\users\\yourname\\videos",
    "c:\\users\\yourname\\downloads"
  ],
  "SensitivityLevel": 3,
  "RealTimeProtection": true,
  "AutoQuarantine": true,
  "LastScanTime": "0001-01-01T00:00:00",
  "LastServiceStopTime": "0001-01-01T00:00:00",
  "TotalScansCount": 0,
  "ExcludedFolderNames": [
    "obj",
    "bin",
    ".git",
    ".vs",
    "node_modules",
    "vendor",
    ".idea"
  ],
  "WhitelistedProcessNames": [],
  "WatchdogEnabled": true,
  "BaseThreatScore": 8,
  "LastScoreUpdateTime": "0001-01-01T00:00:00",
  "NetworkIsolationEnabled": false,
  "EmergencyShutdownEnabled": false
}
```

---

## 🎯 Next Steps

1. **Run Diagnostic Step 1** to identify correct config path
2. **Run Diagnostic Step 2** to verify config file is readable/writable
3. **Run Diagnostic Step 3** to watch for config changes in real-time
4. **Check logs** to see if Save() is being called and if there are any errors
5. **Report findings** so we can identify the root cause

---

**Last Updated:** April 25, 2026  
**Status:** 🔍 Awaiting diagnostic results
