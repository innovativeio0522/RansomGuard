using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Diagnostics;
using System.Timers;
using RansomGuard.Core.Models;
using RansomGuard.Core.Interfaces;
using RansomGuard.Core.Services;
using RansomGuard.Core.Helpers;
using RansomGuard.Core.IPC;

namespace RansomGuard.Service.Engine
{
    public class SentinelEngine : ISystemMonitorService, IDisposable
    {
        private const int ChangeThreshold = 30; // Increased to 30 to reduce false positives
        private const int WindowSeconds = 10;   // Increased to 10s window
        private const int MaxActivityHistory = 100;
        private const int MaxThreatCacheAgeMinutes = 1440; // 24 hours
        
        public bool IsConnected => true;
        public event Action<bool>? ConnectionStatusChanged = delegate { };
        public event Action<FileActivity>? FileActivityDetected;
        public event Action<Threat>? ThreatDetected;
        public event Action<ScanSummary>? ScanCompleted;
        public event Action? ProcessListUpdated = delegate { };

        private readonly List<FileSystemWatcher> _watchers = new();
        private readonly List<FileActivity> _activityHistory = new();
        private readonly List<Threat> _threatHistory = new();
        private readonly object _historyLock = new object();
        private readonly Queue<DateTime> _recentChanges = new();
        private readonly object _recentChangesLock = new object();
        private readonly object _threatDedupLock = new object();
        
        // Track threat report times for periodic cleanup
        private readonly Dictionary<string, DateTime> _reportedThreats = new();

        public bool IsHoneyPotActive { get; set; }
        public bool IsVssShieldActive { get; set; }
        public bool IsPanicModeActive { get; set; }

        private PerformanceCounter? _cpuCounter;
        private PerformanceCounter? _kernelCpuCounter;
        private PerformanceCounter? _userCpuCounter;
        private double _currentKernelCpuUsage = 0;
        private double _currentUserCpuUsage = 0;
        private DateTime _lastScanTime = DateTime.Now.AddDays(-1);
        private System.Timers.Timer? _telemetryTimer;
        private System.Timers.Timer? _cleanupTimer;

        private double _currentCpuUsage = 0;
        private long _currentMemoryUsage = 0;
        private double _currentSystemRamUsedMb = 0;
        private double _currentSystemRamTotalMb = 0;
        private double _lastEntropyScore = 1.5;
        private bool _disposed;

        public SentinelEngine()
        {
            InitializeCounters();
            InitializeWatchers();
            StartTelemetryPolling();
            StartCleanupTimer();
            
            ConfigurationService.Instance.PathsChanged += () => InitializeWatchers();
        }

        private void InitializeCounters()
        {
            try
            {
                if (OperatingSystem.IsWindows())
                {
                    _cpuCounter = new PerformanceCounter("Processor", "% Processor Time", "_Total");
                    _cpuCounter.NextValue();
                    _kernelCpuCounter = new PerformanceCounter("Processor", "% Privileged Time", "_Total");
                    _kernelCpuCounter.NextValue();
                    _userCpuCounter = new PerformanceCounter("Processor", "% User Time", "_Total");
                    _userCpuCounter.NextValue();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to initialize PerformanceCounters: {ex.Message}");
            }
        }

        private void StartTelemetryPolling()
        {
            _telemetryTimer = new System.Timers.Timer(2000);
            _telemetryTimer.Elapsed += (s, e) => {
                _currentCpuUsage = _cpuCounter?.NextValue() ?? 0;
                _currentKernelCpuUsage = _kernelCpuCounter?.NextValue() ?? 0;
                _currentUserCpuUsage = _userCpuCounter?.NextValue() ?? 0;
                
                try {
                    _currentMemoryUsage = Process.GetCurrentProcess().WorkingSet64;
                    
                    // Fetch system RAM stats using NativeMemory helper
                    if (NativeMemory.GetMemoryStatus(out var ms))
                    {
                        _currentSystemRamTotalMb = ms.ullTotalPhys / (1024.0 * 1024.0);
                        _currentSystemRamUsedMb = (ms.ullTotalPhys - ms.ullAvailPhys) / (1024.0 * 1024.0);
                    }
                } catch { }
            };
            _telemetryTimer.Start();
        }

        private void StartCleanupTimer()
        {
            _cleanupTimer = new System.Timers.Timer(3600000); // 1 hour
            _cleanupTimer.Elapsed += (s, e) => {
                CleanupThreatCache();
                ProcessStatsProvider.Instance.Cleanup();
            };
            _cleanupTimer.Start();
        }

        private void CleanupThreatCache()
        {
            lock (_threatDedupLock)
            {
                var now = DateTime.Now;
                var keysToRemove = _reportedThreats
                    .Where(kvp => (now - kvp.Value).TotalMinutes > MaxThreatCacheAgeMinutes)
                    .Select(kvp => kvp.Key)
                    .ToList();

                foreach (var key in keysToRemove)
                    _reportedThreats.Remove(key);
            }
        }

        public void InitializeWatchers()
        {
            lock (_watchers)
            {
                foreach (var watcher in _watchers)
                {
                    watcher.EnableRaisingEvents = false;
                    watcher.Dispose();
                }
                _watchers.Clear();

                var paths = ConfigurationService.Instance.MonitoredPaths;
                foreach (var path in paths)
                {
                    if (Directory.Exists(path))
                    {
                        try
                        {
                            var watcher = new FileSystemWatcher(path)
                            {
                                NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite | NotifyFilters.Attributes,
                                IncludeSubdirectories = true,
                                InternalBufferSize = 65536
                            };

                            watcher.Created += (s, e) => OnFileChanged(e.FullPath, "CREATED");
                            watcher.Changed += (s, e) => OnFileChanged(e.FullPath, "CHANGED");
                            watcher.Deleted += (s, e) => OnFileChanged(e.FullPath, "DELETED");
                            watcher.Renamed += (s, e) => OnFileChanged(e.FullPath, $"RENAMED FROM {e.OldName} TO {e.Name}");

                            watcher.EnableRaisingEvents = true;
                            _watchers.Add(watcher);
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"Failed to start watcher for {path}: {ex.Message}");
                        }
                    }
                }
            }
        }

        private void OnFileChanged(string path, string action)
        {
            // Analyze file extension
            bool suspExt = CheckSuspiciousExtension(path);
            
            // Analyze file entropy if changed or suspicious
            double entropy = 0;
            if (action == "CHANGED" || action.Contains("RENAMED") || suspExt)
            {
                entropy = CalculateShannonEntropy(path);
                _lastEntropyScore = entropy;
            }

            var activity = new FileActivity
            {
                Timestamp = DateTime.Now,
                Action = action,
                FilePath = path,
                Entropy = entropy,
                ProcessName = "System", // Future: Implement PID attribution
                IsSuspicious = suspExt || entropy > 6.0 || CheckSuspiciousPattern(path, action)
            };

            lock (_historyLock)
            {
                _activityHistory.Insert(0, activity);
                if (_activityHistory.Count > MaxActivityHistory) _activityHistory.RemoveAt(MaxActivityHistory);
            }

            FileActivityDetected?.Invoke(activity);

            if (activity.IsSuspicious)
            {
                string reason = suspExt ? "Suspicious Extension" : (entropy > 6.0 ? "High Entropy Data" : "Suspicious Pattern");
                ReportThreat(path, $"{reason} Detected", "System generated alert based on heuristic pattern mismatch.", entropy > 7.0 ? ThreatSeverity.High : ThreatSeverity.Medium);
            }
            
            CheckMassChangeVelocity();
        }

        private double CalculateShannonEntropy(string path)
        {
            try
            {
                if (!File.Exists(path)) return 0;
                
                // For performance, only read first 4KB of files to estimate entropy
                // Encrypted files typically have high entropy throughout.
                byte[] buffer = new byte[4096];
                int bytesRead;
                using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                {
                    bytesRead = stream.Read(buffer, 0, buffer.Length);
                }

                if (bytesRead == 0) return 0;

                var counts = new int[256];
                for (int i = 0; i < bytesRead; i++) counts[buffer[i]]++;

                double entropy = 0;
                for (int i = 0; i < 256; i++)
                {
                    if (counts[i] == 0) continue;
                    double p = (double)counts[i] / bytesRead;
                    entropy -= p * Math.Log2(p);
                }
                return Math.Round(entropy, 2);
            }
            catch { return 0; }
        }

        private void CheckMassChangeVelocity()
        {
            var now = DateTime.Now;
            lock (_recentChangesLock)
            {
                _recentChanges.Enqueue(now);
                while (_recentChanges.Count > 0 && (now - _recentChanges.Peek()).TotalSeconds > WindowSeconds)
                {
                    _recentChanges.Dequeue();
                }

                if (_recentChanges.Count >= ChangeThreshold)
                {
                    ReportThreat("ALL_DRIVES", "MASSIVE FILE ENCRYPTION ACTION DETECTED", "Multiple rapid file changes detected in a short window. Potential active ransomware spray.", ThreatSeverity.Critical);
                    _recentChanges.Clear(); 
                }
            }
        }

        public void ReportThreat(string path, string threatName, string description, ThreatSeverity severity = ThreatSeverity.Medium)
        {
            string threatKey = $"{path}|{threatName}";
            bool shouldReport = false;
            
            lock (_threatDedupLock)
            {
                if (!_reportedThreats.ContainsKey(threatKey))
                {
                    _reportedThreats[threatKey] = DateTime.Now;
                    shouldReport = true;
                }
            }
            
            if (!shouldReport) return;

            var threat = new Threat
            {
                Name = threatName,
                Description = description,
                Path = path,
                ProcessName = "Sentinel Heuristics",
                ProcessId = 0, // In production, this would be looked up via ETW/Kernel
                Severity = severity,
                Timestamp = DateTime.Now
            };

            lock (_historyLock)
            {
                _threatHistory.Insert(0, threat);
            }
            
            ThreatDetected?.Invoke(threat);
        }

        private bool CheckSuspiciousExtension(string path)
        {
            string ext = Path.GetExtension(path).ToLower();
            string[] blocked = { ".locked", ".encrypted", ".crypty", ".wannacry", ".locky", ".crypt", ".enc" };
            return blocked.Contains(ext);
        }

        private bool CheckSuspiciousPattern(string path, string action)
        {
            return action.Contains("RENAMED") && action.ToLower().Contains(".locked");
        }

        public IEnumerable<Threat> GetRecentThreats()
        {
            lock (_historyLock) { return _threatHistory.Take(50).ToList(); }
        }

        public IEnumerable<FileActivity> GetRecentFileActivities()
        {
            lock (_historyLock) { return _activityHistory.Take(50).ToList(); }
        }
        public DateTime GetLastScanTime() => _lastScanTime;

        public async Task PerformQuickScan()
        {
            await Task.Run(() => {
                var paths = ConfigurationService.Instance.MonitoredPaths;
                int filesChecked = 0;
                int threatsFound = 0;

                foreach (var path in paths)
                {
                    if (Directory.Exists(path))
                    {
                        try
                        {
                            var files = Directory.EnumerateFiles(path, "*.*", SearchOption.AllDirectories);
                            foreach (var file in files)
                            {
                                filesChecked++;
                                if (CheckSuspiciousExtension(file))
                                {
                                    threatsFound++;
                                    ReportThreat(file, "Existing Ransomware Artifact Found", "A file with a known ransomware-associated extension was discovered during a system scan.", ThreatSeverity.High);
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"Error scanning {path}: {ex.Message}");
                        }
                    }
                }
                _lastScanTime = DateTime.Now;
                ConfigurationService.Instance.LastScanTime = _lastScanTime;
                ConfigurationService.Instance.Save();

                ScanCompleted?.Invoke(new ScanSummary 
                { 
                    FilesChecked = filesChecked, 
                    ThreatsFound = threatsFound,
                    Timestamp = DateTime.Now
                });
            });
        }

        public IEnumerable<ProcessInfo> GetActiveProcesses()
        {
            var processes = Process.GetProcesses();
            var random = new Random();
            
            return processes.Select(p => {
                try {
                    (bool isTrusted, string signatureStatus) = DetermineProcessIdentity(p);
                    
                    return new ProcessInfo { 
                        Pid = p.Id, 
                        Name = p.ProcessName, 
                        CpuUsage = ProcessStatsProvider.Instance.GetCpuUsage(p),
                        MemoryUsage = p.WorkingSet64,
                        IsTrusted = isTrusted,
                        SignatureStatus = signatureStatus,
                        IoRate = Math.Round(new Random().NextDouble() * 5, 2)
                    };
                } catch { return null; }
            }).Where(p => p != null).Cast<ProcessInfo>().OrderByDescending(p => p.MemoryUsage).Take(50).ToList();
        }

        public double GetSystemCpuUsage() => _currentCpuUsage;
        public long GetSystemMemoryUsage() => _currentMemoryUsage;
        public int GetMonitoredFilesCount() => _watchers.Count;

        public RansomGuard.Core.IPC.TelemetryData GetTelemetry()
        {
            var processes = Process.GetProcesses();
            int threads = 0;
            int total = processes.Length;
            int suspicious = 0;

            foreach(var p in processes)
            {
                try { 
                    threads += p.Threads.Count;
                    if (!p.ProcessName.ToLower().Contains("svchost") && 
                        !p.ProcessName.ToLower().Contains("system") && 
                        !p.ProcessName.ToLower().Contains("windows")) 
                    {
                        // Mock suspicious counting matching GetActiveProcesses heuristics
                    }
                } catch { }
            }
            
            // Generate a consistent dummy trusted percentage for the UI based on running processes
            double trustedPercent = total > 0 ? 99.2 : 100;
            if (total > 0) {
                 suspicious = Math.Max(1, (int)(total * 0.01)); // Mock 1% suspicious
                 trustedPercent = Math.Round(((double)(total - suspicious) / total) * 100, 1);
            }

            return new RansomGuard.Core.IPC.TelemetryData
            {
                CpuUsage = _currentCpuUsage,
                KernelCpuUsage = _currentKernelCpuUsage,
                UserCpuUsage = _currentUserCpuUsage,
                MemoryUsage = _currentMemoryUsage,
                SystemRamUsedMb = _currentSystemRamUsedMb,
                SystemRamTotalMb = _currentSystemRamTotalMb,
                EntropyScore = _lastEntropyScore,
                MonitoredFilesCount = _watchers.Count,
                ProcessesCount = total,
                ActiveThreadsCount = threads,
                TrustedProcessPercent = trustedPercent,
                SuspiciousProcessCount = suspicious,
                IsHoneyPotActive = IsHoneyPotActive,
                IsVssShieldActive = IsVssShieldActive,
                IsPanicModeActive = IsPanicModeActive,
                QuarantinedFilesCount = GetQuarantinedFiles().Count(),
                QuarantineStorageMb = GetQuarantineStorageUsage()
            };
        }

        public IEnumerable<string> GetQuarantinedFiles()
        {
            string quarantinePath = PathConfiguration.QuarantinePath;
            if (!Directory.Exists(quarantinePath)) return Enumerable.Empty<string>();
            
            try
            {
                return Directory.EnumerateFiles(quarantinePath, "*.quarantine");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"GetQuarantinedFiles error: {ex.Message}");
                return Enumerable.Empty<string>();
            }
        }

        public double GetQuarantineStorageUsage()
        {
            string quarantinePath = PathConfiguration.QuarantinePath;
            if (!Directory.Exists(quarantinePath)) return 0;
            
            try
            {
                var files = new DirectoryInfo(quarantinePath).GetFiles("*.quarantine");
                long totalBytes = files.Sum(f => f.Length);
                return totalBytes / (1024.0 * 1024.0); // Return in MB
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"GetQuarantineStorageUsage error: {ex.Message}");
                return 0;
            }
        }

        public async Task KillProcess(int pid)
        {
            await Task.Run(() => {
                try
                {
                    var p = Process.GetProcessById(pid);
                    p.Kill(true);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"KillProcess error: {ex.Message}");
                }
            });
        }

        public async Task QuarantineFile(string filePath)
        {
            await Task.Run(() =>
            {
                try
                {
                    if (!File.Exists(filePath)) return;
                    
                    string quarantineDir = PathConfiguration.QuarantinePath;
                    Directory.CreateDirectory(quarantineDir);
                    
                    var fileName = Path.GetFileName(filePath);
                    var dest = Path.Combine(quarantineDir, fileName + ".quarantine");
                    var metaDest = dest + ".metadata";

                    // Save original metadata for restoration
                    var metadata = $"OriginalPath={filePath}\nQuarantinedAt={DateTime.Now:O}\nFileSize={new FileInfo(filePath).Length}";
                    File.WriteAllText(metaDest, metadata);
                    
                    File.Move(filePath, dest, overwrite: true);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"QuarantineFile error: {ex.Message}");
                }
            });
        }

        public async Task RestoreQuarantinedFile(string quarantinePath)
        {
            await Task.Run(() =>
            {
                try
                {
                    string metaPath = quarantinePath + ".metadata";
                    if (!File.Exists(quarantinePath)) return;

                    string originalPath = string.Empty;
                    if (File.Exists(metaPath))
                    {
                        var lines = File.ReadAllLines(metaPath);
                        foreach (var line in lines)
                        {
                            if (line.StartsWith("OriginalPath=")) originalPath = line.Substring("OriginalPath=".Length);
                        }
                    }

                    if (string.IsNullOrEmpty(originalPath) || originalPath == "Unknown Path")
                    {
                        throw new Exception("Original path not found in metadata.");
                    }

                    string? destDir = Path.GetDirectoryName(originalPath);
                    if (!string.IsNullOrEmpty(destDir))
                    {
                        Directory.CreateDirectory(destDir);
                    }

                    File.Move(quarantinePath, originalPath, overwrite: false);
                    if (File.Exists(metaPath)) File.Delete(metaPath);
                    
                    System.Diagnostics.Debug.WriteLine($"Restored file: {originalPath}");
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"RestoreQuarantinedFile error: {ex.Message}");
                    throw;
                }
            });
        }

        public async Task DeleteQuarantinedFile(string quarantinePath)
        {
            await Task.Run(() =>
            {
                try
                {
                    string metaPath = quarantinePath + ".metadata";
                    if (File.Exists(quarantinePath)) File.Delete(quarantinePath);
                    if (File.Exists(metaPath)) File.Delete(metaPath);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"DeleteQuarantinedFile error: {ex.Message}");
                    throw;
                }
            });
        }

        public async Task ClearSafeFiles()
        {
            await Task.Run(() =>
            {
                try
                {
                    var files = GetQuarantinedFiles().ToList();
                    foreach (var file in files)
                    {
                        var info = new FileInfo(file);
                        // Heuristic: files older than 30 days are purged
                        if (DateTime.Now - info.LastWriteTime > TimeSpan.FromDays(30))
                        {
                            DeleteQuarantinedFile(file).Wait();
                        }
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"ClearSafeFiles error: {ex.Message}");
                }
            });
        }

        public async Task WhitelistProcess(string name)
        {
            if (string.IsNullOrEmpty(name)) return;
            
            await Task.Run(() => {
                if (!ConfigurationService.Instance.WhitelistedProcessNames.Contains(name))
                {
                    ConfigurationService.Instance.WhitelistedProcessNames.Add(name);
                    ConfigurationService.Instance.Save();
                }
            });
        }

        public async Task RemoveWhitelist(string name)
        {
            if (string.IsNullOrEmpty(name)) return;
            
            await Task.Run(() => {
                if (ConfigurationService.Instance.WhitelistedProcessNames.Contains(name))
                {
                    ConfigurationService.Instance.WhitelistedProcessNames.Remove(name);
                    ConfigurationService.Instance.Save();
                }
            });
        }

        private (bool IsTrusted, string Status) DetermineProcessIdentity(Process p)
        {
            try
            {
                string nameLower = p.ProcessName.ToLowerInvariant();
                
                // 1. User Whitelist (Persistent override)
                if (ConfigurationService.Instance.WhitelistedProcessNames.Contains(p.ProcessName))
                    return (true, "User Whitelisted");

                // 2. Critical System Processes (Name-based baseline)
                if (nameLower.Contains("svchost") || nameLower == "system" || nameLower == "idle")
                    return (true, "System Verified");

                if (nameLower.Contains("ransomguard"))
                    return (true, "RansomGuard Self-Check");

                // 2. Path-based Heuristics (Requires SYSTEM privileges to get most paths)
                try
                {
                    string? path = p.MainModule?.FileName?.ToLowerInvariant();
                    if (!string.IsNullOrEmpty(path))
                    {
                        // Verified Operating System folders
                        if (path.Contains(@"c:\windows\"))
                            return (true, "OS Component (Verified)");

                        // Installed Applications
                        if (path.Contains(@"c:\program files\") || path.Contains(@"c:\program files (x86)\"))
                            return (true, "Installed Application");
                        
                        // User Profile Apps (e.g. VS Code, Brave, Discord often install here)
                        if (path.Contains(@"\appdata\local\") || path.Contains(@"\appdata\roaming\"))
                        {
                            // Whitelist common safe developer/user apps
                            if (nameLower == "code" || nameLower == "brave" || nameLower == "dotnet" || 
                                nameLower == "node" || nameLower == "git" || nameLower == "chrome" ||
                                nameLower == "ms-teams" || nameLower == "discord" || nameLower == "antigravity" ||
                                nameLower.Contains("language_server"))
                            {
                                return (true, "User Verified");
                            }
                        }

                        // Security Software (Windows Defender, etc)
                        if (path.Contains(@"\programdata\microsoft\windows defender\"))
                            return (true, "Security Component");
                    }
                }
                catch (System.ComponentModel.Win32Exception)
                {
                    // Access denied even for SYSTEM on some protected processes
                    // Fallback to name check
                    if (nameLower.Contains("windows") || nameLower.Contains("msmpeng") || 
                        nameLower.Contains("mbam") || nameLower.Contains("malwarebytes")) 
                        return (true, "Security/System Component");
                }

                // 3. Fallback for common safe names if path is inaccessible
                if (nameLower == "dotnet" || nameLower == "code" || nameLower == "brave" || 
                    nameLower == "explorer" || nameLower == "antigravity" || nameLower == "msmpeng" ||
                    nameLower.Contains("mbam") || nameLower.Contains("malwarebytes"))
                    return (true, "User Verified");

                return (false, "Unknown Issuer");
            }
            catch
            {
                return (false, "Verification Error");
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            _telemetryTimer?.Stop();
            _telemetryTimer?.Dispose();
            _cleanupTimer?.Stop();
            _cleanupTimer?.Dispose();

            _cpuCounter?.Dispose();

            lock (_watchers)
            {
                foreach (var watcher in _watchers)
                {
                    watcher.EnableRaisingEvents = false;
                    watcher.Dispose();
                }
                _watchers.Clear();
            }
        }
    }

}
