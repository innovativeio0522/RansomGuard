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
using RansomGuard.Core.Configuration;
using RansomGuard.Core.Constants;
using RansomGuard.Core.Helpers;
using RansomGuard.Core.Services;
using System.Collections.Concurrent;
using RansomGuard.Core.IPC;
using RansomGuard.Service.Services;

namespace RansomGuard.Service.Engine
{
    // Alias to avoid ambiguity with System.Diagnostics.PerformanceMonitor
    using PerformanceMonitor = RansomGuard.Core.Services.PerformanceMonitor;
    /// <summary>
    /// The core security engine of RansomGuard. Orchestrates file monitoring,
    /// threat analysis, and response coordination through specialized services.
    /// Refactored: Now acts as an orchestrator, delegating responsibilities to focused services.
    /// </summary>
    public class SentinelEngine : ISystemMonitorService, IDisposable
    {
        private const int MaxDebounceCacheSize = 5000;
        private const int DebounceCleanupIntervalMs = 300000; // 5 minutes

        // ISystemMonitorService implementation
        public bool IsConnected => true;
        public event Action<bool>? ConnectionStatusChanged = delegate { };
        public event Action<FileActivity>? FileActivityDetected;
        public event Action<Threat>? ThreatDetected;
#pragma warning disable CS0067 // Event is never used - Reserved for future scan completion notifications
        public event Action<ScanSummary>? ScanCompleted;
#pragma warning restore CS0067
        public event Action? ProcessListUpdated = delegate { };
        public event Action<TelemetryData>? TelemetryUpdated;
        public event Action<LanPeerListUpdate>? LanPeerListUpdated;

        // Specialized services
        private readonly ITelemetryService _telemetryService;
        private readonly IQuarantineService _quarantine;
        private readonly HistoryManager _historyManager;
        private readonly FileSystemMonitoringService _fileSystemMonitor;
        private readonly ThreatAnalysisService _threatAnalysis;
        private readonly ProcessAttributionService _processAttribution;
        private readonly MassEncryptionDetector _massEncryptionDetector;
        private readonly CriticalResponseService _criticalResponse;
        private readonly IEtwMonitorService? _etwMonitor;
        private readonly LanCircuitBreaker? _lanCircuitBreaker;

        // Event processing pipeline
        private readonly ConcurrentDictionary<string, DateTime> _eventDebounceCache = new();
        private readonly Channel<FileEvent> _eventChannel;
        private readonly ConcurrentDictionary<string, byte> _activeAnalyses = new();
        private readonly CancellationTokenSource _cts = new();
        private Task? _processorTask;
        private bool _isEtwActive;

        private record FileEvent(string Path, string Action, int ProcessId = 0, string ProcessName = "Unknown");

        // State
        public bool IsHoneyPotActive { get; set; } = true;
        public bool IsVssShieldActive { get; set; } = true;

        private bool _isPanicModeActive;
        private DateTime _lastPanicToggle = DateTime.MinValue;
        public bool IsPanicModeActive 
        { 
            get => _isPanicModeActive;
            set 
            {
                if (value == _isPanicModeActive) return;
                
                // Cooldown to prevent UI spam
                if ((DateTime.Now - _lastPanicToggle).TotalSeconds < AppConstants.Timers.PanicModeCooldownSeconds)
                {
                    FileLogger.LogWarning(AppIdentifiers.SentinelEngineLogFile, $"[SENTINEL] Panic Mode toggle blocked: {AppConstants.Timers.PanicModeCooldownSeconds}s cooldown in progress.");
                    return;
                }

                _isPanicModeActive = value;
                _lastPanicToggle = DateTime.Now;
                
                if (value)
                {
                    _criticalResponse.ExecuteCriticalResponse(true, false);
                }
            }
        }

        private DateTime _lastScanTime = ConfigurationService.Instance.LastScanTime;
        private bool _disposed;
        private System.Timers.Timer? _engineCleanupTimer;

        public SentinelEngine(
            ITelemetryService? telemetry = null,
            HistoryManager? history = null,
            IEntropyAnalyzer? entropyAnalyzer = null,
            IProcessIdentityClassifier? processClassifier = null,
            IQuarantineService? quarantine = null,
            LanCircuitBreaker? lanCircuitBreaker = null,
            IEtwMonitorService? etwMonitor = null)
        {
            // Initialize core services
            _telemetryService = telemetry ?? new TelemetryService();
            _historyManager = history ?? new HistoryManager(new HistoryStore());
            _quarantine = quarantine ?? new QuarantineService(new HistoryStore());
            _lanCircuitBreaker = lanCircuitBreaker;
            _etwMonitor = etwMonitor;

            // Initialize specialized services
            var entropyAnalyzerInstance = entropyAnalyzer ?? new EntropyAnalysisService();
            var processClassifierInstance = processClassifier ?? new ProcessIdentityService();
            
            _fileSystemMonitor = new FileSystemMonitoringService(entropyAnalyzerInstance);
            _threatAnalysis = new ThreatAnalysisService(entropyAnalyzerInstance);
            _processAttribution = new ProcessAttributionService(processClassifierInstance);
            _massEncryptionDetector = new MassEncryptionDetector();
            _criticalResponse = new CriticalResponseService();

            // Initialize event processing pipeline with bounded channel for backpressure
            _eventChannel = Channel.CreateBounded<FileEvent>(new BoundedChannelOptions(10000)
            {
                SingleReader = true,
                AllowSynchronousContinuations = false,
                FullMode = BoundedChannelFullMode.DropOldest
            });

            // Wire up file system monitoring events
            _fileSystemMonitor.FileEventDetected += OnFileSystemEvent;
            InitializeWatchers();
            
            // Re-trigger watchers on config change
            ConfigurationService.Instance.PathsChanged += () => InitializeWatchers();

            // Wire up mass encryption detection events
            _massEncryptionDetector.MassEncryptionDetected += OnMassEncryptionDetected;

            // Link telemetry updates
            _telemetryService.TelemetryUpdated += (data) => TelemetryUpdated?.Invoke(GetTelemetry());
            
            // Wire LAN events
            if (_lanCircuitBreaker != null)
            {
                _lanCircuitBreaker.PeerListChanged += (update) => LanPeerListUpdated?.Invoke(update);
            }

            // Wire ETW events
            if (_etwMonitor != null)
            {
                _etwMonitor.FileEventDetected += (e) => OnFileChanged(e.Path, e.Action, e.ProcessId, e.ProcessName);
                try
                {
                    _etwMonitor.Start();
                    _isEtwActive = true;
                }
                catch (Exception ex)
                {
                    FileLogger.LogError(AppIdentifiers.SentinelEngineLogFile, $"[SENTINEL] Failed to start ETW Monitor: {ex.Message}");
                    _isEtwActive = false;
                }
            }
            
            FileLogger.Log(AppIdentifiers.SentinelEngineLogFile, $"[SENTINEL] ETW Monitor initialized: {_etwMonitor != null}, IsEtwActive: {_isEtwActive}");
            
            // Run cleanup timer
            _engineCleanupTimer = new System.Timers.Timer(DebounceCleanupIntervalMs);
            _engineCleanupTimer.Elapsed += (s, e) => {
                _historyManager.CleanupCache();
                CleanupDebounceCache();
            };
            _engineCleanupTimer.Start();

            // Initial historical load
            _historyManager.LoadFromStoreAsync().ContinueWith(task =>
            {
                if (task.IsFaulted && task.Exception != null)
                {
                    FileLogger.LogError(AppIdentifiers.SentinelEngineLogFile, "[SENTINEL] Failed to load history from store", task.Exception);
                }
            }, TaskScheduler.Default);

            _processorTask = Task.Run(() => ProcessEventsAsync(_cts.Token));
        }

        private void CleanupDebounceCache()
        {
            try
            {
                var now = DateTime.Now;
                
                // Remove old entries (older than 10 seconds)
                var keysToRemove = _eventDebounceCache
                    .Where(kvp => (now - kvp.Value).TotalSeconds > 10)
                    .Select(kvp => kvp.Key)
                    .ToList();
                
                foreach (var key in keysToRemove)
                    _eventDebounceCache.TryRemove(key, out _);
                
                // If still too large after cleanup, remove oldest entries
                if (_eventDebounceCache.Count > MaxDebounceCacheSize)
                {
                    var oldest = _eventDebounceCache
                        .OrderBy(kvp => kvp.Value)
                        .Take(_eventDebounceCache.Count - MaxDebounceCacheSize)
                        .Select(kvp => kvp.Key)
                        .ToList();
                    
                    foreach (var key in oldest)
                        _eventDebounceCache.TryRemove(key, out _);
                    
                    Debug.WriteLine($"[SentinelEngine] Debounce cache trimmed from {_eventDebounceCache.Count + oldest.Count} to {_eventDebounceCache.Count} entries");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[SentinelEngine] CleanupDebounceCache error: {ex.Message}");
            }
        }

        public void InitializeWatchers()
        {
            var realTimeProtection = ConfigurationService.Instance.RealTimeProtection;
            var standardPaths = PathConfiguration.GetAllUsersStandardFolders();
            var customPaths = ConfigurationService.Instance.MonitoredPaths;
            
            _fileSystemMonitor.InitializeWatchers(realTimeProtection, standardPaths, customPaths);
        }

        private void OnFileSystemEvent(FileSystemEvent fsEvent)
        {
            OnFileChanged(fsEvent.Path, fsEvent.Action, 0, "Unknown");
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

        internal void OnFileChanged(string path, string action, int processId = 0, string processName = "Unknown")
        {
            var rtp = ConfigurationService.Instance.RealTimeProtection;
            if (!rtp) return;

            // SECURITY BYPASS: Never debounce suspicious files or honeypot activity
            bool isSuspicious = path.Contains("!$RansomGuard_Bait", StringComparison.OrdinalIgnoreCase);

            if (!isSuspicious && IsEventDebounced(path, action))
                return;
            
            // Enqueue event for processing
            bool written = _eventChannel.Writer.TryWrite(new FileEvent(path, action, processId, processName));
            if (!written)
            {
                FileLogger.LogWarning(AppIdentifiers.SentinelEngineLogFile, $"[Sentinel] FAILED to enqueue event (channel full): {path}");
                PerformanceMonitor.Instance.RecordFileAnalysis(0, false, wasDropped: true);
            }
        }

        private async Task ProcessEventsAsync(CancellationToken ct)
        {
            await foreach (var @event in _eventChannel.Reader.ReadAllAsync(ct))
            {
                // Process events in parallel
#pragma warning disable CS4014
                Task.Run(async () =>
                {
                    try
                    {
                        await AnalyzeEventAsync(@event.Path, @event.Action, @event.ProcessId, @event.ProcessName).ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        FileLogger.LogError(AppIdentifiers.SentinelEngineLogFile, $"[PIPELINE] Analysis EXCEPTION for {@event.Path}: {ex.Message}");
                    }
                }, ct).ContinueWith(task =>
                {
                    if (task.IsFaulted && task.Exception != null)
                    {
                        FileLogger.LogError(AppIdentifiers.SentinelEngineLogFile, "[PIPELINE] Event processing task failed", task.Exception);
                    }
                }, TaskScheduler.Default);
#pragma warning restore CS4014
            }
        }

        private async Task AnalyzeEventAsync(string path, string action, int providedProcessId = 0, string providedProcessName = "Unknown")
        {
            var correlationId = StructuredLogger.GenerateCorrelationId();
            
            using (StructuredLogger.BeginOperation("FileAnalysis", correlationId))
            {
                var sw = Stopwatch.StartNew();

                StructuredLogger.LogDebug("Starting file analysis",
                    ("FilePath", path),
                    ("Action", action),
                    ("ProcessId", providedProcessId),
                    ("ProcessName", providedProcessName));

                if (path.Contains("!$RansomGuard_Bait", StringComparison.OrdinalIgnoreCase))
                {
                    // Honeypot bait file hit detected!
                    FileLogger.Log(AppIdentifiers.SentinelEngineLogFile, $"[HoneyPot] Bait hit detected: {path} | Process: {providedProcessName} | IsEtwActive: {_isEtwActive}");
                    
                    // 1. Check if the process is whitelisted (safe system/admin/backup processes)
                    if (IsWhitelistedHoneypotProcess(providedProcessName))
                    {
                        FileLogger.Log(AppIdentifiers.SentinelEngineLogFile, $"[HoneyPot] Ignoring hit from whitelisted process: {providedProcessName}");
                        return; // Ignore safe process activity
                    }

                    // 2. Prevent duplicate alerts from the fallback FileSystemWatcher when ETW is active
                    if (providedProcessName == "Unknown" && _isEtwActive)
                    {
                        return; // Ignore FileSystemWatcher event since ETW will capture it with full process name
                    }

                    // 3. Report the threat
                    ReportThreat(
                        path, 
                        "HONEY POT TRIPWIRE TRIGGERED", 
                        $"An unauthorized process ({providedProcessName}) attempted to access or modify a hidden Sentinel bait file.", 
                        providedProcessName, 
                        providedProcessId, 
                        ThreatSeverity.High
                    );
                    return;
                }
                
                // Deduplicate at the path level
                if (!_activeAnalyses.TryAdd(path, 0)) return;

                // Track active analysis count for performance monitoring
                PerformanceMonitor.Instance.SetActiveAnalysisCount(_activeAnalyses.Count);

                try
                {
                    var config = ConfigurationService.Instance;
                    
                    // Step 1: Threat Analysis
                    var threatResult = _threatAnalysis.AnalyzeFile(path, action, false, config.SensitivityLevel);
                    
                    if (threatResult.ShouldSkip)
                    {
                        FileLogger.LogDebug(AppIdentifiers.SentinelEngineLogFile, $"[TRACE] [{sw.ElapsedMilliseconds}ms] SKIPPED ({threatResult.SkipReason}): {path}");
                        return;
                    }

                    if (threatResult.HasSuspiciousExtension)
                    {
                        FileLogger.LogDebug(AppIdentifiers.SentinelEngineLogFile, $"[TRACE] [{sw.ElapsedMilliseconds}ms] SUSPICIOUS EXTENSION: {path}");
                        StructuredLogger.LogWarning("Suspicious file extension detected",
                            ("FilePath", path),
                            ("Extension", Path.GetExtension(path)));
                    }

                    // Step 2: Process Attribution (only if needed)
                    bool needsProcessId = (providedProcessId == 0) && 
                                         (threatResult.HasSuspiciousExtension || 
                                          (action != "DELETED" && !threatResult.IsMediaFile && !threatResult.IsBinaryFile));
                    
                    ProcessAttributionResult processResult;
                    using (PerformanceMonitor.Instance.MeasureProcessAttribution())
                    {
                        processResult = await _processAttribution.IdentifyProcessAsync(
                            path, action, providedProcessId, providedProcessName, needsProcessId);
                    }

                    // Re-analyze with process trust information
                    threatResult = _threatAnalysis.AnalyzeFile(path, action, processResult.IsTrusted, config.SensitivityLevel);

                    // Step 3: Create Activity Record
                    var activity = new FileActivity
                    {
                        Id = Guid.NewGuid().ToString(),
                        Timestamp = DateTime.Now,
                        FilePath = path,
                        Action = action,
                        ProcessName = processResult.ProcessName,
                        ProcessId = processResult.ProcessId,
                        Entropy = threatResult.Entropy,
                        IsSuspicious = threatResult.IsSuspicious
                    };

                    _historyManager.AddActivity(activity);
                    FileActivityDetected?.Invoke(activity);

                    // Step 4: Handle Suspicious Activity
                    if (activity.IsSuspicious)
                    {
                        string threatAction = config.AutoQuarantine ? "Quarantined (Auto)" : "Detected";
                        
                        StructuredLogger.LogWarning("Suspicious file activity detected",
                            ("FilePath", path),
                            ("Reason", threatResult.ThreatReason),
                            ("Entropy", threatResult.Entropy),
                            ("Threshold", threatResult.EntropyThreshold),
                            ("ProcessName", processResult.ProcessName),
                            ("AutoQuarantine", config.AutoQuarantine));
                        
                        if (config.AutoQuarantine)
                        {
                            try
                            {
                                FileLogger.Log(AppIdentifiers.SentinelEngineLogFile, $"[SENTINEL] Auto-Quarantine TRIGGERED for: {path}");
                                await _quarantine.QuarantineFile(path).ConfigureAwait(false);
                                _telemetryService.IncrementThreatsBlocked();
                                
                                StructuredLogger.LogInfo("File auto-quarantined successfully", ("FilePath", path));
                            }
                            catch (Exception ex)
                            {
                                FileLogger.LogError(AppIdentifiers.SentinelEngineLogFile, $"[SENTINEL] FAILED to quarantine: {path}. Error: {ex.Message}");
                                StructuredLogger.LogError("Auto-quarantine failed", ex, ("FilePath", path));
                                threatAction = "Failed to Quarantine";
                            }
                        }

                        ThreatSeverity severity = (threatResult.IsHighEntropy || threatResult.HasSuspiciousExtension) 
                            ? ThreatSeverity.High : ThreatSeverity.Medium;
                        
                        ReportThreat(path, $"{threatResult.ThreatReason} Detected", 
                            "System generated alert based on heuristic pattern mismatch.",
                            processResult.ProcessName, processResult.ProcessId, severity, threatAction);
                    }

                    // Step 5: Mass Encryption Detection
                    if (action != "DELETED")
                    {
                        _massEncryptionDetector.RecordFileChange(
                            processResult.ProcessName, 
                            processResult.ProcessId, 
                            path, 
                            activity.IsSuspicious, 
                            activity.Entropy,
                            config.SensitivityLevel);
                    }
                    
                    // Record performance metrics
                    PerformanceMonitor.Instance.RecordFileAnalysis(sw.ElapsedMilliseconds, activity.IsSuspicious);
                    PerformanceMonitor.Instance.SetWatcherCount(_fileSystemMonitor.GetTotalWatcherCount());

                    StructuredLogger.LogDebug("File analysis completed",
                        ("FilePath", path),
                        ("DurationMs", sw.ElapsedMilliseconds),
                        ("IsSuspicious", activity.IsSuspicious));
                }
                finally
                {
                    _activeAnalyses.TryRemove(path, out _);
                }
            }
        }

        private void OnMassEncryptionDetected(MassEncryptionAlert alert)
        {
            FileLogger.Log(AppIdentifiers.SentinelEngineLogFile, $"[CRITICAL] MASS ACTIVITY DETECTED! {alert.Reason}. Culprit: {alert.ProcessName} ({alert.ProcessId})");
            PerformanceMonitor.Instance.RecordMassEncryptionAlert();

            // Create threat with user confirmation requirement
            var threat = new Threat
            {
                Id = Guid.NewGuid().ToString(),
                Name = "MASSIVE FILE ACTIVITY DETECTED",
                Description = $"{alert.Reason} detected in a short interval. Culprit: {alert.ProcessName}. {alert.FilesToQuarantine.Count} files identified for protection.",
                Path = "ALL_DRIVES",
                Severity = ThreatSeverity.Critical,
                ProcessName = alert.ProcessName,
                ProcessId = alert.ProcessId,
                Timestamp = alert.Timestamp,
                ActionTaken = "Awaiting Confirmation",
                AffectedFiles = alert.FilesToQuarantine,
                RequiresUserConfirmation = true
            };

            lock (_historyManager)
            {
                _historyManager.AddThreat(threat);
                ThreatDetected?.Invoke(threat);
            }

            // SERVICE-SIDE AUTO-MITIGATION TIMEOUT (5 seconds)
            Task.Run(async () =>
            {
                FileLogger.Log(AppIdentifiers.SentinelEngineLogFile, $"[SENTINEL] Starting 5s auto-mitigation timer for {alert.ProcessName} (PID: {alert.ProcessId})");
                await Task.Delay(5000).ConfigureAwait(false);
                
                // Check if still awaiting confirmation
                var currentThreat = _historyManager.GetRecentThreats().FirstOrDefault(t => t.Id == threat.Id);
                if (currentThreat != null && currentThreat.ActionTaken == "Awaiting Confirmation")
                {
                    FileLogger.Log(AppIdentifiers.SentinelEngineLogFile, $"[SENTINEL] TIMEOUT: No user response. Triggering AUTOMATIC mitigation for {alert.ProcessName}.");
                    await HandleMassEncryptionResponse(threat.Id, shouldMitigate: true, isUserInitiated: false, 
                        alert.ProcessId, alert.ProcessName, alert.FilesToQuarantine).ConfigureAwait(false);
                }
            });

            // VSS Shield Check
#pragma warning disable CS4014
            Task.Run(async () => await _criticalResponse.CheckVssIntegrityAsync()).ContinueWith(task =>
            {
                if (task.IsFaulted && task.Exception != null)
                {
                    FileLogger.LogError(AppIdentifiers.SentinelEngineLogFile, "[SENTINEL] VSS integrity check failed", task.Exception);
                }
            }, TaskScheduler.Default);
#pragma warning restore CS4014
        }

        public void ReportThreat(string path, string threatName, string description, 
            string processName = "Sentinel Heuristics", int processId = 0,
            ThreatSeverity severity = ThreatSeverity.Medium, string actionTaken = "Detected")
        {
            if (string.IsNullOrWhiteSpace(path) || string.IsNullOrWhiteSpace(threatName))
                return;
            
            if (!_historyManager.ShouldReportThreat(path, threatName, actionTaken)) 
                return;

            var threat = new Threat
            {
                Name = threatName,
                Description = description ?? string.Empty,
                Path = path,
                ProcessName = processName ?? "Unknown",
                ProcessId = processId,
                Severity = severity,
                Timestamp = DateTime.Now,
                ActionTaken = actionTaken ?? "Detected"
            };

            _historyManager.AddThreat(threat);
            ThreatDetected?.Invoke(threat);
            PerformanceMonitor.Instance.RecordThreatDetected();

            // If high or critical severity, trigger response protocols
            if (severity >= ThreatSeverity.High)
            {
                var config = ConfigurationService.Instance;
                _criticalResponse.ExecuteCriticalResponse(config.NetworkIsolationEnabled, config.EmergencyShutdownEnabled);
            }
        }

        // ISystemMonitorService implementation - Query methods
        public IEnumerable<Threat> GetRecentThreats() => _historyManager.GetRecentThreats();
        public IEnumerable<FileActivity> GetRecentFileActivities() => _historyManager.GetRecentActivities();
        public DateTime GetLastScanTime() => _lastScanTime;

        public IEnumerable<ProcessInfo> GetActiveProcesses()
        {
            return Process.GetProcesses().Select(p => {
                try {
                    return new ProcessInfo {
                        Pid = p.Id, 
                        Name = p.ProcessName, 
                        CpuUsage = 0,
                        MemoryUsage = p.WorkingSet64, 
                        IsTrusted = false,
                        SignatureStatus = "Unknown",
                        IoRate = Math.Round(Random.Shared.NextDouble() * 5, 2)
                    };
                } catch { return null; }
            }).Where(p => p != null).Cast<ProcessInfo>().OrderByDescending(p => p.MemoryUsage).Take(50).ToList();
        }

        public double GetSystemCpuUsage() => _telemetryService.CurrentCpuUsage;
        public long GetSystemMemoryUsage() => _telemetryService.CurrentMemoryUsage;
        public int GetMonitoredFilesCount() => _fileSystemMonitor.GetTotalWatcherCount();

        public TelemetryData GetTelemetry()
        {
            var data = _telemetryService.GetLatestTelemetry();
            var perfSnap = PerformanceMonitor.Instance.GetSnapshot();
            
            int activeWatcherCount = _fileSystemMonitor.GetActiveWatcherCount();

            data.EntropyScore = _threatAnalysis.LastEntropyScore;
            data.MonitoredFilesCount = _fileSystemMonitor.GetTotalWatcherCount();
            data.ActiveWatchers = activeWatcherCount;
            data.IsHoneyPotActive = IsHoneyPotActive;
            data.IsVssShieldActive = IsVssShieldActive;
            data.IsPanicModeActive = IsPanicModeActive;
            data.QuarantinedFilesCount = GetQuarantinedFiles().Count();
            data.QuarantineStorageMb = GetQuarantineStorageUsage();
            data.IsRealTimeProtectionEnabled = ConfigurationService.Instance.RealTimeProtection;
            data.MonitoredPaths = _fileSystemMonitor.GetMonitoredPaths().ToArray();
            data.LastScanTime = _lastScanTime;
            data.TotalScansCount = ConfigurationService.Instance.TotalScansCount;
            data.FilesPerHour = _historyManager.GetFilesPerHour();
            data.ActiveEndpointsCount = (_lanCircuitBreaker?.PeerCount ?? 0) + 1;

            // Performance metrics
            data.AvgAnalysisMs = perfSnap.AvgAnalysisMs;
            data.P95AnalysisMs = perfSnap.P95AnalysisMs;
            data.AvgEntropyCalcMs = perfSnap.AvgEntropyCalcMs;
            data.AvgIpcWriteMs = perfSnap.AvgIpcWriteMs;
            data.TotalEventsDropped = perfSnap.TotalEventsDropped;
            data.TotalMassEncryptionAlerts = perfSnap.TotalMassEncryptionAlerts;
            
            return data;
        }

        public LanPeerListUpdate GetLanPeerList()
        {
            if (_lanCircuitBreaker == null) return new LanPeerListUpdate();
            return new LanPeerListUpdate
            {
                Peers = _lanCircuitBreaker.GetActivePeers(),
                IsCircuitBroken = _lanCircuitBreaker.IsCircuitBroken,
                TriggerInfo = _lanCircuitBreaker.TriggerInfo
            };
        }

        // ISystemMonitorService implementation - Quarantine operations
        public IEnumerable<string> GetQuarantinedFiles() => _quarantine.GetQuarantinedFiles();
        public double GetQuarantineStorageUsage() => _quarantine.GetStorageUsageMb();
        
        public async Task QuarantineFile(string path)
        {
            await _quarantine.QuarantineFile(path).ConfigureAwait(false);
            _historyManager.UpdateThreatStatus(path, "Quarantined");
            PerformanceMonitor.Instance.RecordFileQuarantined();
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

        // ISystemMonitorService implementation - Process operations
        public async Task KillProcess(int pid)
        {
            await Task.Run(() => { 
                try { 
                    Process.GetProcessById(pid).Kill(true); 
                } catch { } 
            });
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

        // ISystemMonitorService implementation - Threat mitigation
        public async Task MitigateThreat(string threatId)
        {
            var threat = _historyManager.GetThreatById(threatId);
            if (threat == null) return;

            FileLogger.Log(AppIdentifiers.SentinelEngineLogFile, $"[MITIGATION] Manual mitigation triggered for threat: {threat.Id} | Path: {threat.Path}");

            // Kill the process if still running
            if (threat.ProcessId > 0)
            {
                try
                {
                    await KillProcess(threat.ProcessId).ConfigureAwait(false);
                    FileLogger.Log(AppIdentifiers.SentinelEngineLogFile, $"[MITIGATION] Process {threat.ProcessId} ({threat.ProcessName}) killed.");
                }
                catch (Exception ex)
                {
                    FileLogger.LogError(AppIdentifiers.SentinelEngineLogFile, $"[MITIGATION] Failed to kill process {threat.ProcessId}", ex);
                }
            }

            // Quarantine the file
            try
            {
                await _quarantine.QuarantineFile(threat.Path).ConfigureAwait(false);
                _historyManager.UpdateThreatStatusById(threat.Id, "Quarantined");
                ThreatDetected?.Invoke(threat);
                FileLogger.Log(AppIdentifiers.SentinelEngineLogFile, $"[MITIGATION] File {threat.Path} quarantined successfully.");
            }
            catch (Exception ex)
            {
                FileLogger.LogError(AppIdentifiers.SentinelEngineLogFile, $"[MITIGATION] Failed to quarantine file {threat.Path}", ex);
            }
        }

        /// <summary>
        /// Handles mass encryption response: kills the malicious process and quarantines affected files.
        /// Called when user confirms or timeout occurs (5 seconds) for mass encryption threats.
        /// </summary>
        public async Task HandleMassEncryptionResponse(string threatId, bool shouldMitigate, bool isUserInitiated, 
            int processId, string processName, List<string> filesToQuarantine)
        {
            FileLogger.Log(AppIdentifiers.SentinelEngineLogFile, $"[CRITICAL] HandleMassEncryptionResponse triggered. ThreatId: {threatId}, ShouldMitigate={shouldMitigate}, UserInitiated={isUserInitiated}, Process: {processName} (PID: {processId}), Initial Files: {filesToQuarantine.Count}");

            if (string.IsNullOrWhiteSpace(threatId))
            {
                FileLogger.LogWarning(AppIdentifiers.SentinelEngineLogFile, "[CRITICAL] Mass encryption response ignored because threat ID was missing.");
                return;
            }

            if (!shouldMitigate)
            {
                if (_historyManager.TryDeclineMassEncryptionThreat(threatId, out var declinedThreat) && declinedThreat != null)
                {
                    declinedThreat.Description = $"User declined mitigation for suspected mass encryption by {processName}.";
                    ThreatDetected?.Invoke(declinedThreat);
                    FileLogger.Log(AppIdentifiers.SentinelEngineLogFile, $"[CRITICAL] User declined mitigation for threat {threatId}.");
                }
                return;
            }

            if (!_historyManager.TryBeginMassEncryptionMitigation(threatId, out var existingThreat))
            {
                FileLogger.Log(AppIdentifiers.SentinelEngineLogFile, $"[CRITICAL] Mitigation ignored for threat {threatId} because it was already claimed or resolved.");
                return;
            }

            // Sweep for additional files modified by this process
            var extraFiles = _massEncryptionDetector.GetAdditionalFilesByProcess(processId, processName);
            foreach (var file in extraFiles)
            {
                if (!filesToQuarantine.Contains(file, StringComparer.OrdinalIgnoreCase))
                {
                    filesToQuarantine.Add(file);
                }
            }
            
            _massEncryptionDetector.ClearRecentChanges();

            FileLogger.Log(AppIdentifiers.SentinelEngineLogFile, $"[CRITICAL] Mass encryption protection expanded to {filesToQuarantine.Count} total files.");

            // KILL THE PROCESS IMMEDIATELY
            if (processId > 0 && processName != "explorer" && !processName.Contains("RansomGuard"))
            {
                try
                {
                    FileLogger.Log(AppIdentifiers.SentinelEngineLogFile, $"[CRITICAL] PROACTIVE DEFENSE: Terminating malicious process {processName} (PID: {processId})");
                    await KillProcess(processId).ConfigureAwait(false);
                    FileLogger.Log(AppIdentifiers.SentinelEngineLogFile, $"[CRITICAL] Process {processId} terminated successfully.");
                }
                catch (Exception ex)
                {
                    FileLogger.LogError(AppIdentifiers.SentinelEngineLogFile, $"[CRITICAL] Failed to terminate process {processId}", ex);
                }
            }

            // QUARANTINE ALL AFFECTED FILES
            FileLogger.Log(AppIdentifiers.SentinelEngineLogFile, $"[CRITICAL] PROACTIVE DEFENSE: Quarantining {filesToQuarantine.Count} suspicious files.");
            
            int successCount = 0;
            int failCount = 0;
            
            foreach (var file in filesToQuarantine)
            {
                FileLogger.Log(AppIdentifiers.SentinelEngineLogFile, $"[QUARANTINE] Processing: {file}");
                try
                {
                    string targetFile = file;
                    
                    if (!File.Exists(targetFile))
                    {
                        FileLogger.Log(AppIdentifiers.SentinelEngineLogFile, $"[QUARANTINE] Exact match not found: {targetFile}. Searching for variants...");
                        string directory = Path.GetDirectoryName(file) ?? string.Empty;
                        string fileName = Path.GetFileName(file);
                        
                        if (!string.IsNullOrEmpty(directory) && Directory.Exists(directory))
                        {
                            // Look for the file with ANY extra extension
                            string searchPattern = fileName + ".*";
                            var potentialFiles = Directory.GetFiles(directory, searchPattern);
                            
                            if (potentialFiles.Length > 0)
                            {
                                targetFile = potentialFiles[0];
                                FileLogger.Log(AppIdentifiers.SentinelEngineLogFile, $"[QUARANTINE] Found renamed target via pattern {searchPattern}: {targetFile}");
                            }
                            else
                            {
                                // Strip the LAST extension and search again
                                int lastDot = fileName.LastIndexOf('.');
                                if (lastDot > 0)
                                {
                                    string baseName = fileName.Substring(0, lastDot);
                                    searchPattern = baseName + ".*";
                                    potentialFiles = Directory.GetFiles(directory, searchPattern);
                                    if (potentialFiles.Length > 0)
                                    {
                                        targetFile = potentialFiles[0];
                                        FileLogger.Log(AppIdentifiers.SentinelEngineLogFile, $"[QUARANTINE] Found target via base pattern {searchPattern}: {targetFile}");
                                    }
                                }
                            }
                        }
                    }

                    if (File.Exists(targetFile))
                    {
                        FileLogger.Log(AppIdentifiers.SentinelEngineLogFile, $"[QUARANTINE] Final target verified, moving to quarantine: {targetFile}");
                        await _quarantine.QuarantineFile(targetFile).ConfigureAwait(false);
                        successCount++;
                    }
                    else
                    {
                        FileLogger.LogError(AppIdentifiers.SentinelEngineLogFile, $"[QUARANTINE] FAILED: File not found after all strategies: {file}");
                        failCount++;
                    }
                }
                catch (Exception ex)
                {
                    FileLogger.LogError(AppIdentifiers.SentinelEngineLogFile, $"[QUARANTINE] EXCEPTION processing {file}: {ex.Message}");
                    failCount++;
                }
            }

            FileLogger.Log(AppIdentifiers.SentinelEngineLogFile, $"[CRITICAL] Mass encryption response complete. Quarantined: {successCount}, Failed: {failCount}");

            // Update threat status
            if (existingThreat != null)
            {
                existingThreat.ActionTaken = isUserInitiated ? "Mitigated" : "Mitigated (Auto)";
                existingThreat.RequiresUserConfirmation = false;
                existingThreat.Description = $"Multiple rapid file changes detected. Culprit: {processName}. {successCount} files quarantined, {failCount} failed.";
                _historyManager.UpdateThreatStatusById(existingThreat.Id, existingThreat.ActionTaken);
                ThreatDetected?.Invoke(existingThreat);
            }
            else
            {
                ReportThreat("ALL_DRIVES", "MASSIVE FILE ENCRYPTION ACTION DETECTED", 
                    $"Multiple rapid file changes detected. Culprit: {processName}. {successCount} files quarantined, {failCount} failed.", 
                    processName, processId, ThreatSeverity.Critical, isUserInitiated ? "Mitigated" : "Mitigated (Auto)");
            }

            // Trigger Critical Response
            var config = ConfigurationService.Instance;
            _criticalResponse.ExecuteCriticalResponse(config.NetworkIsolationEnabled, config.EmergencyShutdownEnabled);

            // LAN Circuit Breaker
            if (_lanCircuitBreaker != null)
            {
                await _lanCircuitBreaker.TriggerCircuitBreakAsync($"Mass encryption by {processName} (PID:{processId}). {successCount} files quarantined.").ConfigureAwait(false);
            }
            
            // VSS Shield Check
#pragma warning disable CS4014
            Task.Run(async () => await _criticalResponse.CheckVssIntegrityAsync()).ContinueWith(task =>
            {
                if (task.IsFaulted && task.Exception != null)
                {
                    FileLogger.LogError(AppIdentifiers.SentinelEngineLogFile, "[SENTINEL] VSS integrity check failed", task.Exception);
                }
            }, TaskScheduler.Default);
#pragma warning restore CS4014
        }

        private static readonly string[] WhitelistedHoneypotProcesses = new[]
        {
            "explorer.exe",
            "searchindexer.exe",
            "msmpeng.exe",
            "onedrive.exe",
            "rgservice.exe",
            "ransomguard.service.exe",
            "rgworker.exe",
            "ransomguard.watchdog.exe",
            "ransomguard.exe",
            "rgui.exe",
            "system",
            "lsass.exe",
            "svchost.exe",
            "msiexec.exe",
            "devenv.exe",
            "dotnet.exe"
        };

        private bool IsWhitelistedHoneypotProcess(string processName)
        {
            if (string.IsNullOrEmpty(processName)) return false;
            
            var name = processName.ToLowerInvariant();
            
            // Check exact match or suffix (e.g. "onedrive.exe" or "onedrive")
            return WhitelistedHoneypotProcesses.Any(p => 
                name == p || 
                name == Path.GetFileNameWithoutExtension(p));
        }

        public async Task ClearActivityHistory()
        {
            await _historyManager.ClearHistory().ConfigureAwait(false);
            TelemetryUpdated?.Invoke(GetTelemetry());
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            
            _cts.Cancel();
            _cts.Dispose();
            _engineCleanupTimer?.Dispose();
            _telemetryService.Dispose();
            _historyManager.Dispose();
            _fileSystemMonitor.Dispose();
            _processAttribution.Dispose();
            _lanCircuitBreaker?.Dispose();
            
            if (_etwMonitor != null)
            {
                try { _etwMonitor.Stop(); } catch { }
                _isEtwActive = false;
            }
        }
    }
}
