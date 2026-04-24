using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using RansomGuard.Core.Models;
using RansomGuard.Core.Interfaces;
using RansomGuard.Core.Services;
using System.Collections.Concurrent;
using RansomGuard.Core.IPC;
using RansomGuard.Service.Services;
using RansomGuard.Core.Helpers;

namespace RansomGuard.Service.Engine
{
    /// <summary>
    /// The core security engine of RansomGuard. Handles real-time file monitoring,
    /// heuristic analysis, and threat classification.
    /// Decomposed (#29): Telemetry and History management are now handled by specialized services.
    /// </summary>
    public class SentinelEngine : ISystemMonitorService, IDisposable
    {
        private const int WindowSeconds = 10;
        private const int MaxActivityHistory = 100;

        private static int GetChangeThreshold() => ConfigurationService.Instance.SensitivityLevel switch
        {
            1 => 50, 2 => 40, 3 => 30, 4 => 20, _ => 30
        };

        private double GetEntropyThreshold(string path, bool isTrustedProcess = false, bool isScan = false)
        {
            bool isMedia = _entropyAnalyzer.IsMediaFile(path);
            bool isBinary = _entropyAnalyzer.IsHighEntropyExtension(path);

            double baseThreshold = ConfigurationService.Instance.SensitivityLevel switch
            {
                1 => 7.8, // Low
                2 => 7.5, // Medium
                3 => 7.2, // High
                4 => 6.8, // Paranoid
                _ => 7.2
            };

            if (isMedia || isBinary)
            {
                if (isTrustedProcess) return 7.99;
                if (isScan) return 7.95;
                return 7.85; 
            }
            
            return baseThreshold;
        }

        public bool IsConnected => true;
        public event Action<bool>? ConnectionStatusChanged = delegate { };
        public event Action<FileActivity>? FileActivityDetected;
        public event Action<Threat>? ThreatDetected;
        public event Action<ScanSummary>? ScanCompleted;
        public event Action? ProcessListUpdated = delegate { };
        public event Action<TelemetryData>? TelemetryUpdated;

        private readonly List<FileSystemWatcher> _watchers = new();
        private readonly HistoryManager _historyManager;
        private readonly ITelemetryService _telemetryService;
        private readonly IQuarantineService _quarantine;
        private readonly IEntropyAnalyzer _entropyAnalyzer;
        private readonly IProcessIdentityClassifier _processClassifier;
        
        private readonly Queue<DateTime> _recentChanges = new();
        private readonly object _recentChangesLock = new();
        private readonly ConcurrentDictionary<string, DateTime> _eventDebounceCache = new();
        private readonly Channel<FileEvent> _eventChannel;
        private readonly CancellationTokenSource _cts = new();
        private Task? _processorTask;

        private record FileEvent(string Path, string Action);

        public bool IsHoneyPotActive { get; set; } = true;
        public bool IsVssShieldActive { get; set; } = true;
        public bool IsPanicModeActive { get; set; }

        private double _lastEntropyScore = 1.5;
        private DateTime _lastScanTime = ConfigurationService.Instance.LastScanTime;
        private bool _disposed;
        private System.Timers.Timer? _engineCleanupTimer;

        public SentinelEngine(
            ITelemetryService? telemetry = null,
            HistoryManager? history = null,
            IEntropyAnalyzer? entropyAnalyzer = null,
            IProcessIdentityClassifier? processClassifier = null,
            IQuarantineService? quarantine = null)
        {
            _telemetryService = telemetry ?? new TelemetryService();
            _historyManager = history ?? new HistoryManager(new HistoryStore());
            _entropyAnalyzer = entropyAnalyzer ?? new EntropyAnalysisService();
            _processClassifier = processClassifier ?? new ProcessIdentityService();
            _quarantine = quarantine ?? new QuarantineService(new HistoryStore());

            // Initialize background processing pipeline
            _eventChannel = Channel.CreateUnbounded<FileEvent>(new UnboundedChannelOptions
            {
                SingleReader = true,
                AllowSynchronousContinuations = false
            });

            InitializeWatchers();
            
            // Re-trigger watchers on config change
            ConfigurationService.Instance.PathsChanged += () => InitializeWatchers();

            // Link telemetry updates
            _telemetryService.TelemetryUpdated += (data) => TelemetryUpdated?.Invoke(GetTelemetry());
            
            _engineCleanupTimer = new System.Timers.Timer(3600000); // 1 hour
            _engineCleanupTimer.Elapsed += (s, e) => {
                _historyManager.CleanupCache();
                CleanupDebounceCache();
            };
            _engineCleanupTimer.Start();

            // Initial historical load
            _ = _historyManager.LoadFromStoreAsync();

            _processorTask = Task.Run(() => ProcessEventsAsync(_cts.Token));
        }

        private void CleanupDebounceCache()
        {
            var now = DateTime.Now;
            var keysToRemove = _eventDebounceCache
                .Where(kvp => (now - kvp.Value).TotalSeconds > 10)
                .Select(kvp => kvp.Key)
                .ToList();
            foreach (var key in keysToRemove)
                _eventDebounceCache.TryRemove(key, out _);
        }

        public void InitializeWatchers()
        {
            lock (_watchers)
            {
                foreach (var w in _watchers) { w.EnableRaisingEvents = false; w.Dispose(); }
                _watchers.Clear();

                if (!ConfigurationService.Instance.RealTimeProtection) return;

                foreach (var rawPath in ConfigurationService.Instance.MonitoredPaths.Distinct())
                {
                    string path = Path.GetFullPath(rawPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).ToLowerInvariant();
                    if (!Directory.Exists(path)) continue;

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
                    } catch { }
                }
            }
        }

        private bool IsEventDebounced(string path, string action)
        {
            var now = DateTime.Now;
            string key = $"{path.ToLowerInvariant()}|{action}";
            bool isNew = true;
            _eventDebounceCache.AddOrUpdate(key, now, (k, lastTime) => {
                if ((now - lastTime).TotalMilliseconds < 500) { isNew = false; return lastTime; }
                return now;
            });
            return !isNew;
        }

        internal void OnFileChanged(string path, string action)
        {
            if (!ConfigurationService.Instance.RealTimeProtection || IsEventDebounced(path, action)) return;
            
            // Fast-path: Just enqueue the event and return to the watcher thread immediately
            _eventChannel.Writer.TryWrite(new FileEvent(path, action));
        }

        private async Task ProcessEventsAsync(CancellationToken ct)
        {
            await foreach (var @event in _eventChannel.Reader.ReadAllAsync(ct))
            {
                try
                {
                    await AnalyzeEventAsync(@event.Path, @event.Action);
                }
                catch (OperationCanceledException) { break; }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[SentinelEngine] Analysis error: {ex.Message}");
                }
            }
        }

        private async Task AnalyzeEventAsync(string path, string action)
        {
            if (path.Contains("!$RansomGuard_Bait", StringComparison.OrdinalIgnoreCase)) return;
            
            // Skip excluded directories
            if (_entropyAnalyzer.ShouldSkipDirectory(path) || path.Split(Path.DirectorySeparatorChar).Any(part => _entropyAnalyzer.ShouldSkipDirectory(part))) return;

            if (Directory.Exists(path) && !File.Exists(path)) return;

            bool suspExt = _entropyAnalyzer.IsSuspiciousExtension(path);
            bool isMedia = _entropyAnalyzer.IsMediaFile(path);
            bool isBinary = _entropyAnalyzer.IsHighEntropyExtension(path);
            double entropy = 0;

            // --- Process Attribution ---
            string culpritProcess = "Unknown";
            bool isTrustedProcess = false;

            try
            {
                // Wait briefly for the file handle to be released/stable if needed
                await Task.Delay(50); 
                var processes = _processClassifier.GetProcessesUsingFile(path);
                foreach (var p in processes)
                {
                    if (p.ProcessName.Contains("RansomGuard", StringComparison.OrdinalIgnoreCase)) continue;
                    culpritProcess = p.ProcessName;
                    var identity = _processClassifier.DetermineIdentity(p);
                    
                    if (!action.Contains("RENAMED"))
                    {
                        isTrustedProcess = identity.IsTrusted;
                        if (isTrustedProcess) break;
                    }
                }
            }
            catch { }

            if (action == "CHANGED" || action == "CREATED" || action.Contains("RENAMED") || suspExt || isMedia || isBinary)
            {
                entropy = _entropyAnalyzer.CalculateShannonEntropy(path);
                _lastEntropyScore = entropy;
            }

            double threshold = GetEntropyThreshold(path, isTrustedProcess, isScan: false);
            bool isEntropyAlert = entropy > threshold;
            bool isRenameAlert = _entropyAnalyzer.IsSuspiciousRenamePattern(action);
            
            var activity = new FileActivity
            {
                Id = Guid.NewGuid().ToString(),
                Timestamp = DateTime.Now,
                FilePath = path,
                Action = action,
                ProcessName = culpritProcess,
                Entropy = entropy,
                IsSuspicious = isEntropyAlert || isRenameAlert
            };

            _historyManager.AddActivity(activity);
            FileActivityDetected?.Invoke(activity);

            if (activity.IsSuspicious)
            {
                string reason = suspExt ? "Suspicious Extension" : (isEntropyAlert ? "High Entropy Data" : "Suspicious Pattern");
                string threatAction = ConfigurationService.Instance.AutoQuarantine ? "Quarantined" : "Detected";
                
                if (ConfigurationService.Instance.AutoQuarantine) _ = _quarantine.QuarantineFile(path);

                ReportThreat(path, $"{reason} Detected", "System generated alert based on heuristic pattern mismatch.",
                    culpritProcess, 0, isEntropyAlert ? ThreatSeverity.High : ThreatSeverity.Medium, threatAction);
            }

            CheckMassChangeVelocity();
        }

        private void CheckMassChangeVelocity()
        {
            var now = DateTime.Now;
            lock (_recentChangesLock)
            {
                _recentChanges.Enqueue(now);
                while (_recentChanges.Count > 0 && (now - _recentChanges.Peek()).TotalSeconds > WindowSeconds) _recentChanges.Dequeue();
                if (_recentChanges.Count > GetChangeThreshold())
                {
                    ReportThreat("ALL_DRIVES", "MASSIVE FILE ENCRYPTION ACTION DETECTED", 
                        $"Multiple rapid file changes detected in a short window (threshold: {GetChangeThreshold()} in {WindowSeconds}s).", 
                        "System", 0, ThreatSeverity.Critical);
                    _recentChanges.Clear();
                    
                    // Trigger Critical Response for mass encryption
                    ExecuteCriticalResponse();
                }
            }
        }

        private void ExecuteCriticalResponse()
        {
            var config = ConfigurationService.Instance;

            if (config.NetworkIsolationEnabled)
            {
                IsolateNetwork();
            }

            if (config.EmergencyShutdownEnabled)
            {
                EmergencyShutdown();
            }
        }

        private void IsolateNetwork()
        {
            try
            {
                Debug.WriteLine("[SentinelEngine] CRITICAL: Triggering Network Isolation...");
                // Disables all active network adapters using netsh
                var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = "powershell.exe",
                        Arguments = "-Command \"Get-NetAdapter | Where-Object { $_.Status -eq 'Up' } | Disable-NetAdapter -Confirm:$false\"",
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        Verb = "runas"
                    }
                };
                process.Start();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[SentinelEngine] Failed to isolate network: {ex.Message}");
            }
        }

        private void EmergencyShutdown()
        {
            try
            {
                Debug.WriteLine("[SentinelEngine] CRITICAL: Triggering Emergency Shutdown...");
                Process.Start(new ProcessStartInfo
                {
                    FileName = "shutdown.exe",
                    Arguments = "/s /f /t 5 /c \"RansomGuard: Critical Threat Detected. Emergency Shutdown triggered to prevent data loss.\"",
                    CreateNoWindow = true,
                    UseShellExecute = false
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[SentinelEngine] Failed to trigger shutdown: {ex.Message}");
            }
        }

        public void ReportThreat(string path, string threatName, string description, 
            string processName = "Sentinel Heuristics", int processId = 0,
            ThreatSeverity severity = ThreatSeverity.Medium, string actionTaken = "Detected")
        {
            if (!_historyManager.ShouldReportThreat(path, threatName)) return;

            var threat = new Threat
            {
                Name = threatName,
                Description = description,
                Path = path,
                ProcessName = processName,
                ProcessId = processId,
                Severity = severity,
                Timestamp = DateTime.Now,
                ActionTaken = actionTaken
            };

            _historyManager.AddThreat(threat);
            ThreatDetected?.Invoke(threat);

            // If the threat is critical, trigger response protocols
            if (severity == ThreatSeverity.Critical)
            {
                ExecuteCriticalResponse();
            }
        }

        public IEnumerable<Threat> GetRecentThreats() => _historyManager.GetRecentThreats();
        public IEnumerable<FileActivity> GetRecentFileActivities() => _historyManager.GetRecentActivities();
        public DateTime GetLastScanTime() => _lastScanTime;




        public IEnumerable<ProcessInfo> GetActiveProcesses()
        {
            // This is still fairly expensive, but we delegate identity to the classifier
            return Process.GetProcesses().Select(p => {
                try {
                    (bool isTrusted, string status) = _processClassifier.DetermineIdentity(p);
                    return new ProcessInfo {
                        Pid = p.Id, Name = p.ProcessName, CpuUsage = 0, // Simplified for this view
                        MemoryUsage = p.WorkingSet64, IsTrusted = isTrusted, SignatureStatus = status,
                        IoRate = Math.Round(Random.Shared.NextDouble() * 5, 2)
                    };
                } catch { return null; }
            }).Where(p => p != null).Cast<ProcessInfo>().OrderByDescending(p => p.MemoryUsage).Take(50).ToList();
        }

        public double GetSystemCpuUsage() => _telemetryService.CurrentCpuUsage;
        public long GetSystemMemoryUsage() => _telemetryService.CurrentMemoryUsage;
        public int GetMonitoredFilesCount() => _watchers.Count;

        public TelemetryData GetTelemetry()
        {
            var data = _telemetryService.GetLatestTelemetry();
            
            int activeWatcherCount;
            lock (_watchers) { activeWatcherCount = _watchers.Count(w => w.EnableRaisingEvents); }

            data.EntropyScore = _lastEntropyScore;
            data.MonitoredFilesCount = _watchers.Count;
            data.ActiveWatchers = activeWatcherCount;
            data.IsHoneyPotActive = IsHoneyPotActive;
            data.IsVssShieldActive = IsVssShieldActive;
            data.IsPanicModeActive = IsPanicModeActive;
            data.QuarantinedFilesCount = GetQuarantinedFiles().Count();
            data.QuarantineStorageMb = GetQuarantineStorageUsage();
            data.IsRealTimeProtectionEnabled = ConfigurationService.Instance.RealTimeProtection;
            data.MonitoredPaths = _watchers.Select(w => w.Path).ToArray();
            data.LastScanTime = _lastScanTime;
            data.TotalScansCount = ConfigurationService.Instance.TotalScansCount;
            
            return data;
        }

        public IEnumerable<string> GetQuarantinedFiles() => _quarantine.GetQuarantinedFiles();
        public double GetQuarantineStorageUsage() => _quarantine.GetStorageUsageMb();
        
        public async Task QuarantineFile(string path)
        {
            await _quarantine.QuarantineFile(path).ConfigureAwait(false);
            _historyManager.UpdateThreatStatus(path, "Quarantined");
        }

        public async Task RestoreQuarantinedFile(string path)
        {
            await _quarantine.RestoreQuarantinedFile(path).ConfigureAwait(false);
            _historyManager.UpdateThreatStatus(path, "Restored");
        }

        public async Task DeleteQuarantinedFile(string path)
        {
            await _quarantine.DeleteQuarantinedFile(path).ConfigureAwait(false);
            _historyManager.UpdateThreatStatus(path, "Deleted");
        }

        public async Task ClearSafeFiles() => await _quarantine.ClearOldFiles().ConfigureAwait(false);

        public async Task KillProcess(int pid)
        {
            await Task.Run(() => { try { Process.GetProcessById(pid).Kill(true); } catch { } });
        }

        public async Task WhitelistProcess(string name)
        {
            if (string.IsNullOrEmpty(name)) return;
            await Task.Run(() => {
                if (!ConfigurationService.Instance.WhitelistedProcessNames.Contains(name)) {
                    ConfigurationService.Instance.WhitelistedProcessNames.Add(name);
                    ConfigurationService.Instance.Save();
                }
            });
        }

        public async Task RemoveWhitelist(string name)
        {
            if (string.IsNullOrEmpty(name)) return;
            await Task.Run(() => {
                if (ConfigurationService.Instance.WhitelistedProcessNames.Contains(name)) {
                    ConfigurationService.Instance.WhitelistedProcessNames.Remove(name);
                    ConfigurationService.Instance.Save();
                }
            });
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _engineCleanupTimer?.Dispose();
            _telemetryService.Dispose();
            _historyManager.Dispose();
            lock (_watchers) { foreach (var w in _watchers) w.Dispose(); _watchers.Clear(); }
        }
    }
}
