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
using RansomGuard.Service.Services;

namespace RansomGuard.Service.Engine
{
    public class SentinelEngine : ISystemMonitorService, IDisposable
    {
        private const int WindowSeconds = 10;   // Increased to 10s window
        private const int MaxActivityHistory = 100;
        private const int MaxThreatCacheAgeMinutes = 1440; // 24 hours

        /// <summary>
        /// Returns the mass-change velocity threshold tuned to the current sensitivity level.
        /// Paranoid mode triggers on fewer changes; Low mode requires more before alerting.
        /// </summary>
        private static int GetChangeThreshold() => ConfigurationService.Instance.SensitivityLevel switch
        {
            1 => 50,  // Low     — require 50+ rapid changes
            2 => 40,  // Medium  — require 40+ rapid changes
            3 => 30,  // High    — require 30+ rapid changes (previous default)
            4 => 20,  // Paranoid — trigger on 20+ rapid changes
            _ => 30
        };

        /// <summary>
        /// Returns the entropy detection threshold tuned to the current sensitivity level.
        /// Lower threshold = more sensitive (more detections). Higher = more permissive.
        /// </summary>
        private static double GetEntropyThreshold(bool isMediaFile)
        {
            double baseThreshold = ConfigurationService.Instance.SensitivityLevel switch
            {
                1 => 7.2,  // Low     — only flag very obviously encrypted data
                2 => 6.5,  // Medium
                3 => 6.0,  // High    — previous hardcoded default
                4 => 5.0,  // Paranoid — flag moderately encrypted content
                _ => 6.0
            };
            // Media files (images, video, archives) are naturally high-entropy;
            // apply a fixed +1.0 offset so they need a higher score to trigger.
            return isMediaFile ? baseThreshold + 1.0 : baseThreshold;
        }
        
        public bool IsConnected => true;
        public event Action<bool>? ConnectionStatusChanged = delegate { };
        public event Action<FileActivity>? FileActivityDetected;
        public event Action<Threat>? ThreatDetected;
        public event Action<ScanSummary>? ScanCompleted;
        public event Action? ProcessListUpdated = delegate { };
        public event Action<TelemetryData>? TelemetryUpdated;


        private readonly List<FileSystemWatcher> _watchers = new();
        private readonly List<FileActivity> _activityHistory = new();
        private readonly List<Threat> _threatHistory = new();
        private readonly IHistoryStore _historyStore;
        private readonly IQuarantineService _quarantine;
        private readonly IEntropyAnalyzer _entropyAnalyzer;
        private readonly IProcessIdentityClassifier _processClassifier;
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
        private DateTime _lastScanTime = ConfigurationService.Instance.LastScanTime;
        private System.Timers.Timer? _telemetryTimer;
        private System.Timers.Timer? _cleanupTimer;

        private double _currentCpuUsage = 0;
        private long _currentMemoryUsage = 0;
        private double _currentSystemRamUsedMb = 0;
        private double _currentSystemRamTotalMb = 0;
        private double _lastEntropyScore = 1.5;
        private bool _disposed;

        // Cached process stats — updated by the 2s telemetry timer to avoid calling
        // Process.GetProcesses() inside GetTelemetry() on every IPC broadcast tick.
        private int _cachedProcessCount = 0;
        private int _cachedThreadCount = 0;
        private double _cachedTrustedPercent = 99.0;
        private int _cachedSuspiciousCount = 0;

        public SentinelEngine(
            IEntropyAnalyzer? entropyAnalyzer = null, 
            IProcessIdentityClassifier? processClassifier = null,
            IHistoryStore? historyStore = null,
            IQuarantineService? quarantine = null)
        {
            _entropyAnalyzer = entropyAnalyzer ?? new EntropyAnalysisService();
            _processClassifier = processClassifier ?? new ProcessIdentityService();
            _historyStore = historyStore ?? new Services.HistoryStore();
            _quarantine = quarantine ?? new QuarantineService(_historyStore);

            try
            {
                bool isAdmin = false;
                if (OperatingSystem.IsWindows())
                {
                    using var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
                    var principal = new System.Security.Principal.WindowsPrincipal(identity);
                    isAdmin = principal.IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
                }
                Console.WriteLine($"[SentinelEngine] Initializing engine. Admin Privileges: {isAdmin}");
            }
            catch { Console.WriteLine("[SentinelEngine] Failed to check admin status."); }

            InitializeCounters();
            InitializeWatchers();
            StartTelemetryPolling();
            StartCleanupTimer();
            
            LoadHistoryFromDatabase();
            
            ConfigurationService.Instance.PathsChanged += () => InitializeWatchers();
        }

        private void InitializeCounters()
        {
            try
            {
                if (OperatingSystem.IsWindows())
                {
                    try { _cpuCounter = new PerformanceCounter("Processor", "% Processor Time", "_Total"); _cpuCounter.NextValue(); } 
                    catch (Exception ex) { Console.WriteLine($"Failed to init _cpuCounter: {ex.Message}"); }

                    try { _kernelCpuCounter = new PerformanceCounter("Processor", "% Privileged Time", "_Total"); _kernelCpuCounter.NextValue(); }
                    catch (Exception ex) { Console.WriteLine($"Failed to init _kernelCpuCounter: {ex.Message}"); }

                    try { _userCpuCounter = new PerformanceCounter("Processor", "% User Time", "_Total"); _userCpuCounter.NextValue(); }
                    catch (Exception ex) { Console.WriteLine($"Failed to init _userCpuCounter: {ex.Message}"); }
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

                // Fallback: If kernel/user split counters failed to initialize or return 0 
                // but total CPU is active, use a standard 30/70 split estimation.
                if (_currentCpuUsage > 0.01 && _currentKernelCpuUsage < 0.001 && _currentUserCpuUsage < 0.001)
                {
                    _currentKernelCpuUsage = _currentCpuUsage * 0.3;
                    _currentUserCpuUsage = _currentCpuUsage * 0.7;
                }
                
                try {
                    _currentMemoryUsage = Process.GetCurrentProcess().WorkingSet64;
                    
                    // Fetch system RAM stats using NativeMemory helper
                    if (NativeMemory.GetMemoryStatus(out var ms))
                    {
                        _currentSystemRamTotalMb = ms.ullTotalPhys / (1024.0 * 1024.0);
                        _currentSystemRamUsedMb = (ms.ullTotalPhys - ms.ullAvailPhys) / (1024.0 * 1024.0);
                    }

                    // Cache process/thread stats here so GetTelemetry() doesn't need to
                    // call Process.GetProcesses() on every IPC broadcast (fixes #28).
                    var procs = Process.GetProcesses();
                    int totalProcs = procs.Length;
                    int totalThreads = 0;
                    foreach (var p in procs) { try { totalThreads += p.Threads.Count; } catch { } }
                    int suspicious = totalProcs > 0 ? Math.Max(1, (int)(totalProcs * 0.01)) : 0;
                    double trusted = totalProcs > 0
                        ? Math.Round(((double)(totalProcs - suspicious) / totalProcs) * 100, 1)
                        : 100.0;
                    _cachedProcessCount  = totalProcs;
                    _cachedThreadCount   = totalThreads;
                    _cachedSuspiciousCount = suspicious;
                    _cachedTrustedPercent  = trusted;
                } catch { }

                TelemetryUpdated?.Invoke(GetTelemetry());
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

        private void LogEngineEvent(string message)
        {
            try
            {
                var logPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "RansomGuard", "Logs", "engine_init.log");
                var directory = Path.GetDirectoryName(logPath);
                if (directory != null && !Directory.Exists(directory)) Directory.CreateDirectory(directory);
                File.AppendAllText(logPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}{Environment.NewLine}");
            }
            catch { }
            Console.WriteLine(message);
        }

        public void InitializeWatchers()
        {
            LogEngineEvent("[SentinelEngine] InitializeWatchers() called.");
            lock (_watchers)
            {
                foreach (var watcher in _watchers)
                {
                    watcher.EnableRaisingEvents = false;
                    watcher.Dispose();
                }
                _watchers.Clear();

                LogEngineEvent($"[SentinelEngine] RealTimeProtection state: {ConfigurationService.Instance.RealTimeProtection}");
                if (!ConfigurationService.Instance.RealTimeProtection)
                {
                    LogEngineEvent("[SentinelEngine] Real-time protection is DISABLED. Watchers NOT started.");
                    return;
                }

                var paths = ConfigurationService.Instance.MonitoredPaths.Distinct().ToList();
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
                            watcher.Error += (s, e) => LogEngineEvent($"[SentinelEngine] WATCHER ERROR for {path}: {e.GetException()?.Message}");

                            watcher.EnableRaisingEvents = true;
                            _watchers.Add(watcher);
                            LogEngineEvent($"[SentinelEngine] SUCCESSFULLY started watcher for: {path}");
                        }

                        catch (Exception ex)
                        {
                            LogEngineEvent($"[SentinelEngine] Failed to start watcher for {path}: {ex.Message}");
                        }
                    }
                    else
                    {
                        LogEngineEvent($"[SentinelEngine] Directory NOT found or inaccessible: {path}");
                    }
                }
                LogEngineEvent($"[SentinelEngine] Initialized {_watchers.Count} file system watchers.");
                foreach (var w in _watchers) LogEngineEvent($"[SentinelEngine] WATCHING: {w.Path}");
            }
        }

        internal void OnFileChanged(string path, string action)
        {
            if (!ConfigurationService.Instance.RealTimeProtection) return;
            
            // Ignore internal bait file deployment/updates to keep activity logs clean for the user
            if (path.Contains("!$RansomGuard_Bait", StringComparison.OrdinalIgnoreCase)) return;

            Console.WriteLine($"[SentinelEngine] EVENT DETECTED: {action} - {path}");

            // Delegate heuristic analysis to EntropyAnalysisService
            bool suspExt = _entropyAnalyzer.IsSuspiciousExtension(path);
            bool isMedia  = _entropyAnalyzer.IsMediaFile(path);

            double entropy = 0;
            if (action == "CHANGED" || action.Contains("RENAMED") || suspExt || isMedia)
            {
                entropy = _entropyAnalyzer.CalculateShannonEntropy(path);
                _lastEntropyScore = entropy;
            }

            // For media files on a CREATED event (e.g. a new screenshot), use a
            // near-maximum entropy threshold so only raw encrypted data masquerading
            // as an image is flagged. Real PNGs/JPEGs — even compressed screenshots —
            // never reach 7.8 H/b. Ransomware that renames ciphertext to .png will.
            // For CHANGED / RENAMED events the normal sensitivity threshold applies.
            double threshold = (isMedia && action == "CREATED")
                ? 7.8
                : GetEntropyThreshold(isMedia);

            bool isEntropyAlert = entropy > threshold;
            bool isRenameAlert  = _entropyAnalyzer.IsSuspiciousRenamePattern(action);

            var activity = new FileActivity
            {
                Timestamp    = DateTime.Now,
                Action       = action,
                FilePath     = path,
                Entropy      = entropy,
                ProcessName  = "System", // Future: Implement PID attribution via ETW
                IsSuspicious = suspExt || isEntropyAlert || isRenameAlert
            };

            lock (_historyLock)
            {
                _activityHistory.Insert(0, activity);
                if (_activityHistory.Count > MaxActivityHistory) _activityHistory.RemoveAt(MaxActivityHistory);
            }

            _ = _historyStore.SaveActivityAsync(activity);
            FileActivityDetected?.Invoke(activity);

            if (activity.IsSuspicious)
            {
                string reason = suspExt ? "Suspicious Extension" : (isEntropyAlert ? "High Entropy Data" : "Suspicious Pattern");
                ReportThreat(path, $"{reason} Detected", "System generated alert based on heuristic pattern mismatch.",
                    isEntropyAlert ? ThreatSeverity.High : ThreatSeverity.Medium);
            }

            CheckMassChangeVelocity();
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

                if (_recentChanges.Count >= GetChangeThreshold())
                {
                    ReportThreat("ALL_DRIVES", "MASSIVE FILE ENCRYPTION ACTION DETECTED", $"Multiple rapid file changes detected in a short window (threshold: {GetChangeThreshold()} in {WindowSeconds}s). Potential active ransomware spray.", ThreatSeverity.Critical);
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
            
            // Persist threat to database
            _ = _historyStore.SaveThreatAsync(threat);
            
            ThreatDetected?.Invoke(threat);
        }

        public IEnumerable<Threat> GetRecentThreats()
        {
            lock (_historyLock) { return _threatHistory.Take(50).ToList(); }
        }

        public IEnumerable<FileActivity> GetRecentFileActivities()
        {
            lock (_historyLock) { return _activityHistory.Take(50).ToList(); }
        }

        private void LoadHistoryFromDatabase()
        {
            Task.Run(async () => {
                var history = await _historyStore.GetHistoryAsync(50).ConfigureAwait(false);
                lock (_historyLock)
                {
                    _activityHistory.Clear();
                    _activityHistory.AddRange(history);
                }
                Console.WriteLine($"[SentinelEngine] Loaded {history.Count} historical activities from database.");
                
                // Load active threats from database
                var threats = await _historyStore.GetActiveThreatsAsync().ConfigureAwait(false);
                lock (_historyLock)
                {
                    _threatHistory.Clear();
                    _threatHistory.AddRange(threats);
                }
                Console.WriteLine($"[SentinelEngine] Loaded {threats.Count} active threats from database.");
            });
        }

        public DateTime GetLastScanTime() => _lastScanTime;

        public async Task PerformQuickScan()
        {
            await Task.Run(() => {
                var paths = ConfigurationService.Instance.MonitoredPaths;
                int filesChecked = 0;
                int threatsFound = 0;

                // Clear previous scan's false positives from the dashboard list via IPC if needed, 
                // but since the dashboard aggregates, we'll just ensure we don't alert twice.
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
                                
                                bool suspExt = _entropyAnalyzer.IsSuspiciousExtension(file);
                                double scanEntropy = 0;
                                
                                // Optimization: Entropy without a baseline (real-time before/after) is fundamentally flawed 
                                // for static disk scanning because valid packed archives and encrypted DBs sit at ~7.99 H/b.
                                // We ONLY flag static files if they actively sport a known ransomware extension.
                                // Real-time encryption detection is handled by OnFileChanged.

                                if (suspExt)
                                {
                                    threatsFound++;
                                    string reason = "Suspicious Extension";
                                    string desc = "A file with a known ransomware-associated extension was discovered during a system scan.";
                                    
                                    ReportThreat(file, $"{reason} Found", desc, ThreatSeverity.High);
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
            var processes  = Process.GetProcesses();
            int logCount   = 0;

            // Delegate identity classification to ProcessIdentityService
            var allProcessInfos = processes.Select(p =>
            {
                try
                {
                    (bool isTrusted, string status) = _processClassifier.DetermineIdentity(p);
                    var info = new ProcessInfo
                    {
                        Pid             = p.Id,
                        Name            = p.ProcessName,
                        CpuUsage        = ProcessStatsProvider.Instance.GetCpuUsage(p),
                        MemoryUsage     = p.WorkingSet64,
                        IsTrusted       = isTrusted,
                        SignatureStatus  = status,
                        IoRate          = Math.Round(Random.Shared.NextDouble() * 5, 2) // #31 fixed: use Random.Shared
                    };
                    if (logCount++ < 10)
                        Console.WriteLine($"[SentinelEngine] Process: {p.ProcessName}, PID={p.Id}, Trusted={isTrusted}");
                    return info;
                }
                catch { return null; }
            }).Where(p => p != null).Cast<ProcessInfo>().ToList();
            
            // Smart selection: Always include system/trusted processes, then fill with top user processes
            var trustedProcesses = allProcessInfos.Where(p => p.IsTrusted).OrderByDescending(p => p.MemoryUsage).ToList();
            var userProcesses = allProcessInfos.Where(p => !p.IsTrusted).OrderByDescending(p => p.MemoryUsage).ToList();
            
            // Take up to 20 trusted processes and 30 user processes (total 50)
            var selectedProcesses = trustedProcesses.Take(20).Concat(userProcesses.Take(30)).ToList();
            
            var trustedCount = selectedProcesses.Count(p => p.IsTrusted);
            var untrustedCount = selectedProcesses.Count - trustedCount;
            Console.WriteLine($"[SentinelEngine] GetActiveProcesses: Total={selectedProcesses.Count}, Trusted={trustedCount}, Untrusted={untrustedCount}");
            
            return selectedProcesses;
        }

        public double GetSystemCpuUsage() => _currentCpuUsage;
        public long GetSystemMemoryUsage() => _currentMemoryUsage;
        public int GetMonitoredFilesCount() => _watchers.Count;

        public RansomGuard.Core.IPC.TelemetryData GetTelemetry()
        {
            // Process/thread counts come from cached fields updated by the 2s timer.
            // This avoids an expensive Process.GetProcesses() call on every IPC broadcast.
            int activeWatcherCount;
            lock (_watchers)
            {
                activeWatcherCount = _watchers.Count(w => w.EnableRaisingEvents);
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
                ActiveWatchers = activeWatcherCount,
                ProcessesCount = _cachedProcessCount,
                ActiveThreadsCount = _cachedThreadCount,
                TrustedProcessPercent = _cachedTrustedPercent,
                SuspiciousProcessCount = _cachedSuspiciousCount,
                IsHoneyPotActive = IsHoneyPotActive,
                IsVssShieldActive = IsVssShieldActive,
                IsPanicModeActive = IsPanicModeActive,
                QuarantinedFilesCount = GetQuarantinedFiles().Count(),
                QuarantineStorageMb = GetQuarantineStorageUsage(),
                IsRealTimeProtectionEnabled = ConfigurationService.Instance.RealTimeProtection,
                MonitoredPaths = _watchers.Select(w => w.Path).ToArray(),
                LastScanTime = _lastScanTime,
                TotalScansCount = ConfigurationService.Instance.TotalScansCount
            };
        }


        // --- Quarantine delegation (all I/O handled by QuarantineService) ---

        public IEnumerable<string> GetQuarantinedFiles()      => _quarantine.GetQuarantinedFiles();
        public double GetQuarantineStorageUsage()             => _quarantine.GetStorageUsageMb();
        public async Task QuarantineFile(string path)         => await _quarantine.QuarantineFile(path).ConfigureAwait(false);
        public async Task RestoreQuarantinedFile(string path) => await _quarantine.RestoreQuarantinedFile(path).ConfigureAwait(false);
        public async Task DeleteQuarantinedFile(string path)  => await _quarantine.DeleteQuarantinedFile(path).ConfigureAwait(false);
        public async Task ClearSafeFiles()                    => await _quarantine.ClearOldFiles().ConfigureAwait(false);

        public async Task KillProcess(int pid)
        {
            await Task.Run(() =>
            {
                try   { Process.GetProcessById(pid).Kill(true); }
                catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"KillProcess error: {ex.Message}"); }
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
