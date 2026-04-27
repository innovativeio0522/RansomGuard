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
        private const int MaxDebounceCacheSize = 5000; // Maximum entries in debounce cache
        private const int DebounceCleanupIntervalMs = 300000; // 5 minutes

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
#pragma warning disable CS0067 // Event is never used - Reserved for future scan completion notifications
        public event Action<ScanSummary>? ScanCompleted;
#pragma warning restore CS0067
        public event Action? ProcessListUpdated = delegate { };
        public event Action<TelemetryData>? TelemetryUpdated;

        private readonly ITelemetryService _telemetryService;
        private readonly IQuarantineService _quarantine;
        private readonly IEntropyAnalyzer _entropyAnalyzer;
        private readonly IProcessIdentityClassifier _processClassifier;
        private readonly HistoryManager _historyManager;
        private readonly List<FileSystemWatcher> _watchers = new();
        private readonly ConcurrentDictionary<string, DateTime> _eventDebounceCache = new();
        private readonly Channel<FileEvent> _eventChannel = Channel.CreateUnbounded<FileEvent>();
        
        // Gatekeeper to prevent overlapping expensive process identification calls
        private readonly SemaphoreSlim _processIdSemaphore = new(1, 1);
        private readonly ConcurrentDictionary<string, byte> _activeAnalyses = new();
        private readonly CancellationTokenSource _cts = new();
        private Task? _processorTask;

        private record FileEvent(string Path, string Action);
        private readonly Queue<DateTime> _recentChanges = new();
        private readonly object _recentChangesLock = new();

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
            
            // Run cleanup more frequently (every 5 minutes instead of 1 hour)
            _engineCleanupTimer = new System.Timers.Timer(DebounceCleanupIntervalMs);
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
            lock (_watchers)
            {
                foreach (var w in _watchers) { w.EnableRaisingEvents = false; w.Dispose(); }
                _watchers.Clear();

                var realTimeProtection = ConfigurationService.Instance.RealTimeProtection;
                FileLogger.Log("sentinel_engine.log", $"[InitializeWatchers] RealTimeProtection={realTimeProtection}");
                if (!realTimeProtection) return;

                // Always watch all-users standard folders (Documents, Desktop, Downloads etc.)
                // regardless of what is in config. These are built-in and cannot be removed.
                var standardPaths = PathConfiguration.GetAllUsersStandardFolders();
                FileLogger.Log("sentinel_engine.log", $"[InitializeWatchers] Standard folders found: {standardPaths.Count()}");

                // Custom paths from the shared ProgramData config
                var currentConfigPaths = ConfigurationService.Instance.MonitoredPaths;

                // Merge both sources, deduplicated
                // CRITICAL FIX: Do NOT use ToLowerInvariant() here. FileSystemWatcher on Windows
                // has a known bug where it drops events if the casing of the Path does not perfectly
                // match the casing of the actual directory on disk.
                var allPaths = standardPaths
                    .Concat(currentConfigPaths)
                    .Select(p => Path.GetFullPath(p).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

                FileLogger.Log("sentinel_engine.log", $"[InitializeWatchers] Total deduplicated paths to watch: {allPaths.Count}");
                foreach (var p in allPaths) FileLogger.Log("sentinel_engine.log", $"  -> {p}");

                foreach (var rawPath in allPaths)
                {
                    string path = rawPath;
                    // Force uppercase drive letter - critical for FileSystemWatcher reliability on Windows
                    if (path.Length >= 2 && path[1] == ':' && char.IsLower(path[0]))
                    {
                        path = char.ToUpper(path[0]) + path.Substring(1);
                    }

                    if (!Directory.Exists(path)) continue;

                    FileSystemWatcher? watcher = null;
                    try
                    {
                        watcher = new FileSystemWatcher(path)
                        {
                            NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite | NotifyFilters.Attributes,
                            IncludeSubdirectories = true,
                            InternalBufferSize = 65536
                        };

                        watcher.Created += (s, e) => OnFileChanged(e.FullPath, "CREATED");
                        watcher.Changed += (s, e) => OnFileChanged(e.FullPath, "CHANGED");
                        watcher.Deleted += (s, e) => OnFileChanged(e.FullPath, "DELETED");
                        watcher.Renamed += (s, e) => OnFileChanged(e.FullPath, $"RENAMED FROM {e.OldName} TO {e.Name}");

                        // Enable events BEFORE adding to list - if this throws, watcher won't be in list
                        watcher.EnableRaisingEvents = true;
                        
                        // Only add to list after successful initialization
                        _watchers.Add(watcher);
                        FileLogger.Log("sentinel_engine.log", $"[InitializeWatchers] Watcher ACTIVE for: {path}");
                        watcher = null; // Ownership transferred - don't dispose in catch block
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"[SentinelEngine] Failed to create watcher for {path}: {ex.Message}");
                        // Dispose the watcher if it wasn't successfully added to the list
                        watcher?.Dispose();
                    }
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
            var rtp = ConfigurationService.Instance.RealTimeProtection;
            
            // SECURITY BYPASS: Never debounce suspicious extensions or honeypot activity.
            // We must analyze every single one of these even if they happen rapidly.
            bool isSuspicious = _entropyAnalyzer.IsSuspiciousExtension(path) || 
                              path.Contains("!$RansomGuard_Bait", StringComparison.OrdinalIgnoreCase);

            if (!rtp) return;

            if (!isSuspicious && IsEventDebounced(path, action))
            {
                // Log skips for regular files only if needed, to avoid log bloat
                // FileLogger.Log("sentinel_engine.log", $"[Sentinel] Event skipped (debounced): {path} | {action}");
                return;
            }
            
            // Fast-path: Just enqueue the event and return to the watcher thread immediately
            bool written = _eventChannel.Writer.TryWrite(new FileEvent(path, action));
            if (!written)
            {
                FileLogger.LogWarning("sentinel_engine.log", $"[Sentinel] FAILED to enqueue event (channel full): {path}");
            }
        }

        private async Task ProcessEventsAsync(CancellationToken ct)
        {
            await foreach (var @event in _eventChannel.Reader.ReadAllAsync(ct))
            {
                // Process events in parallel to prevent one slow check (like Restart Manager)
                // from blocking the entire engine pipeline.
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await AnalyzeEventAsync(@event.Path, @event.Action);
                    }
                    catch (Exception ex)
                    {
                        FileLogger.LogError("sentinel_engine.log", $"[PIPELINE] Analysis EXCEPTION for {@event.Path}: {ex.Message}");
                    }
                }, ct);
            }
        }

        private async Task AnalyzeEventAsync(string path, string action)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();

            if (path.Contains("!$RansomGuard_Bait", StringComparison.OrdinalIgnoreCase)) return;
            
            // Deduplicate at the path level to prevent parallel analysis of the same file 
            // triggered by multiple FSW events (Created + Changed)
            if (!_activeAnalyses.TryAdd(path, 0)) return;

            try
            {
                // Skip excluded directories
                bool skipDir = _entropyAnalyzer.ShouldSkipDirectory(path) || path.Split(Path.DirectorySeparatorChar).Any(part => _entropyAnalyzer.ShouldSkipDirectory(part));
                if (skipDir)
                {
                    FileLogger.Log("sentinel_engine.log", $"[TRACE] [{sw.ElapsedMilliseconds}ms] SKIPPED (excluded dir): {path}");
                    return;
                }

                if (Directory.Exists(path) && !File.Exists(path))
                {
                    FileLogger.Log("sentinel_engine.log", $"[TRACE] [{sw.ElapsedMilliseconds}ms] SKIPPED (is directory): {path}");
                    return;
                }

                bool suspExt = _entropyAnalyzer.IsSuspiciousExtension(path);
                if (suspExt) FileLogger.Log("sentinel_engine.log", $"[TRACE] [{sw.ElapsedMilliseconds}ms] SUSPICIOUS EXTENSION: {path}");
                
                FileLogger.Log("sentinel_engine.log", $"[TRACE] [{sw.ElapsedMilliseconds}ms] Starting deep analysis for: {path}");

                bool isMedia = _entropyAnalyzer.IsMediaFile(path);
                bool isBinary = _entropyAnalyzer.IsHighEntropyExtension(path);
                double entropy = 0;

                FileLogger.Log("sentinel_engine.log", $"[TRACE] [{sw.ElapsedMilliseconds}ms] Extension check done. isMedia={isMedia}, isBinary={isBinary}");

                // --- Process Attribution with Multiple Strategies ---
                string culpritProcess = "Unknown";
                bool isTrustedProcess = false;

                // PERFORMANCE OPTIMIZATION: Only perform expensive process identification
                // if the file is actually suspicious (suspicious extension, high entropy, or honeypot).
                // This prevents system-wide "Restart Manager" gridlock from background system activity.
                bool needsProcessId = suspExt || (action != "DELETED" && !isMedia && !isBinary);

                if (needsProcessId)
                {
                    try
                    {
                        // Use a semaphore to ensure we don't spam the Windows Restart Manager service
                        // with hundreds of concurrent requests.
                        await _processIdSemaphore.WaitAsync().ConfigureAwait(false);
                        
                        try
                        {
                            var processes = new List<Process>();

                            // Strategy 2: Use Restart Manager with timeout to find owner
                            if (processes.Count == 0)
                            {
                                try
                                {
                                    var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
                                    var processTask = _processClassifier.GetProcessesUsingFileAsync(path);

                                    // Wait for process task or timeout
                                    if (await Task.WhenAny(processTask, Task.Delay(-1, cts.Token)) == processTask)
                                    {
                                        processes = await processTask;
                                    }
                                    else
                                    {
                                        FileLogger.LogWarning("sentinel_engine.log", $"[Sentinel] Process identification timed out for: {path}");
                                    }
                                }
                                catch (OperationCanceledException)
                                {
                                    FileLogger.LogWarning("sentinel_engine.log", $"[Sentinel] Process identification cancelled (timeout) for: {path}");
                                }
                            }

                            if (processes != null && processes.Count > 0)
                            {
                                var primary = processes[0];
                                culpritProcess = primary.ProcessName;
                                isTrustedProcess = _processClassifier.DetermineIdentity(primary).IsTrusted;
                            }

                            // Strategy 3: If still no process, use heuristics based on file location and type
                            if (processes == null || processes.Count == 0)
                            {
                                culpritProcess = InferProcessFromContext(path, action);
                            }
                            else
                            {
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
                        }
                        catch (Exception ex)
                        {
                            FileLogger.LogError("sentinel_engine.log", $"[Sentinel] Error in process identification: {ex.Message}");
                        }
                        finally
                        {
                            _processIdSemaphore.Release();
                        }
                    }
                    catch (Exception ex)
                    {
                        FileLogger.LogError("sentinel_engine.log", $"[Sentinel] Outer error in process attribution: {ex.Message}");
                    }
                }
                // Process identification done.

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
                    IsSuspicious = isEntropyAlert || isRenameAlert || suspExt
                };

                _historyManager.AddActivity(activity);
                FileActivityDetected?.Invoke(activity);

                if (activity.IsSuspicious)
                {
                    var protectionSettings = ConfigurationService.Instance;
                    string reason = suspExt ? "Suspicious Extension" : (isEntropyAlert ? "High Entropy Data" : "Suspicious Pattern");
                    string threatAction = protectionSettings.AutoQuarantine ? "Quarantined" : "Detected";
                    
                    if (protectionSettings.AutoQuarantine) _ = _quarantine.QuarantineFile(path);

                    ThreatSeverity severity = (isEntropyAlert || suspExt) ? ThreatSeverity.High : ThreatSeverity.Medium;
                    ReportThreat(path, $"{reason} Detected", "System generated alert based on heuristic pattern mismatch.",
                        culpritProcess, 0, severity, threatAction);
                }

                CheckMassChangeVelocity();
            }
            finally
            {
                _activeAnalyses.TryRemove(path, out _);
            }
        }

        /// <summary>
        /// Infers the likely process responsible for a file operation based on context clues
        /// when direct process detection fails.
        /// </summary>
        private string InferProcessFromContext(string path, string action)
        {
            try
            {
                string pathLower = path.ToLowerInvariant();
                string fileName = Path.GetFileName(pathLower);
                string extension = Path.GetExtension(pathLower);
                
                // Browser downloads
                if (pathLower.Contains(@"\downloads\"))
                {
                    if (fileName.Contains("reddit") || fileName.Contains("www."))
                        return "Browser (Download)";
                    return "explorer";
                }
                
                // Screenshots
                if (pathLower.Contains(@"\pictures\screenshots\") || fileName.Contains("screenshot"))
                    return "SnippingTool";
                
                // Desktop files
                if (pathLower.Contains(@"\desktop\"))
                {
                    if (extension == ".txt") return "notepad";
                    if (extension == ".bmp" || extension == ".png") return "mspaint";
                    return "explorer";
                }
                
                // Documents folder
                if (pathLower.Contains(@"\documents\"))
                {
                    if (extension == ".txt") return "notepad";
                    if (extension == ".docx" || extension == ".doc") return "WINWORD";
                    if (extension == ".xlsx" || extension == ".xls") return "EXCEL";
                    if (extension == ".pdf") return "AcroRd32";
                    return "explorer";
                }
                
                // Temp files
                if (pathLower.Contains(@"\temp\") || pathLower.Contains(@"\tmp\"))
                    return "System Process";
                
                // AppData operations
                if (pathLower.Contains(@"\appdata\"))
                    return "Application";
                
                // Default fallback
                return "explorer";
            }
            catch
            {
                return "Unknown";
            }
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
            FileLogger.Log("sentinel_engine.log", $"[CRITICAL] ExecuteCriticalResponse triggered. NetworkIsolation={config.NetworkIsolationEnabled}, Shutdown={config.EmergencyShutdownEnabled}");

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
                FileLogger.Log("sentinel_engine.log", "[CRITICAL] Running Network Isolation command...");
                // Disables all active network adapters using powershell
                var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = "powershell.exe",
                        Arguments = "-NoProfile -ExecutionPolicy Bypass -Command \"Get-NetAdapter | Where-Object { $_.Status -eq 'Up' } | Disable-NetAdapter -Confirm:$false\"",
                        UseShellExecute = false,
                        CreateNoWindow = true
                    }
                };
                process.Start();
                FileLogger.Log("sentinel_engine.log", "[CRITICAL] Network Isolation command started successfully.");
            }
            catch (Exception ex)
            {
                FileLogger.LogError("sentinel_engine.log", $"[CRITICAL] Failed to trigger network isolation: {ex.Message}");
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
            // Validate required parameters
            if (string.IsNullOrWhiteSpace(path))
            {
                System.Diagnostics.Debug.WriteLine("[SentinelEngine] ReportThreat called with empty path");
                return;
            }
            
            if (string.IsNullOrWhiteSpace(threatName))
            {
                System.Diagnostics.Debug.WriteLine("[SentinelEngine] ReportThreat called with empty threat name");
                return;
            }
            
            if (!_historyManager.ShouldReportThreat(path, threatName, actionTaken)) return;

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

            // If the threat is high or critical, trigger response protocols
            if (severity >= ThreatSeverity.High)
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
            data.FilesPerHour = _historyManager.GetFilesPerHour();
            
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

        public async Task MitigateThreat(string threatId)
        {
            var threat = _historyManager.GetThreatById(threatId);
            if (threat == null) return;

            FileLogger.Log("sentinel_engine.log", $"[MITIGATION] Manual mitigation triggered for threat: {threat.Id} | Path: {threat.Path}");

            // 1. Kill the process if it's still running
            if (threat.ProcessId > 0)
            {
                try
                {
                    await KillProcess(threat.ProcessId).ConfigureAwait(false);
                    FileLogger.Log("sentinel_engine.log", $"[MITIGATION] Process {threat.ProcessId} ({threat.ProcessName}) killed.");
                }
                catch (Exception ex)
                {
                    FileLogger.LogError("sentinel_engine.log", $"[MITIGATION] Failed to kill process {threat.ProcessId}", ex);
                }
            }

            // 2. Quarantine the file
            try
            {
                await _quarantine.QuarantineFile(threat.Path).ConfigureAwait(false);
                _historyManager.UpdateThreatStatusById(threat.Id, "Quarantined");
                
                // Signal UI to update
                ThreatDetected?.Invoke(threat);
                
                FileLogger.Log("sentinel_engine.log", $"[MITIGATION] File {threat.Path} quarantined successfully.");
            }
            catch (Exception ex)
            {
                FileLogger.LogError("sentinel_engine.log", $"[MITIGATION] Failed to quarantine file {threat.Path}", ex);
            }
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
