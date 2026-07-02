using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using RansomGuard.Core.Models;
using RansomGuard.Core.Helpers;
using RansomGuard.Core.Constants;
using RansomGuard.Core.Services;
using RansomGuard.Core.Configuration;
using RansomGuard.Service.Services;

namespace RansomGuard.Service.Engine
{
    /// <summary>
    /// Manages in-memory caching and deduplication of threats and file activities,
    /// serving as a bridge between the real-time engine and the persistent HistoryStore.
    /// </summary>
    public class HistoryManager : IDisposable
    {
        private const int MaxActivityHistory = AppConstants.Limits.MaxActivityHistory;
        private const int MaxThreatCacheAgeMinutes = 60; // 1 hour (reduced from 24 hours)
        private const int MaxThreatCacheSize = AppConstants.Limits.MaxThreatCacheSize; // Maximum entries in dedup cache
        
        private readonly List<FileActivity> _activityHistory = new();
        private readonly List<Threat> _threatHistory = new();
        private readonly Dictionary<string, DateTime> _reportedThreats = new();
        
        private readonly object _historyLock = new();
        private readonly object _threatDedupLock = new();
        private readonly IHistoryStore _historyStore;
        private bool _disposed;

        public HistoryManager(IHistoryStore historyStore)
        {
            _historyStore = historyStore;
        }

        public async Task LoadFromStoreAsync()
        {
            using (StructuredLogger.BeginOperation("LoadHistoryFromStore"))
            {
                StructuredLogger.LogInfo("Loading history from persistent store");
                
                var history = await _historyStore.GetHistoryAsync(50).ConfigureAwait(false);
                lock (_historyLock)
                {
                    _activityHistory.Clear();
                    _activityHistory.AddRange(history);
                }

                var threats = await _historyStore.GetActiveThreatsAsync().ConfigureAwait(false);
                lock (_historyLock)
                {
                    _threatHistory.Clear();
                    _threatHistory.AddRange(threats);
                    // NOTE: We intentionally do NOT pre-populate _reportedThreats here.
                    // That dictionary is session-only dedup for real-time watcher spam prevention.
                    // Pre-populating it from the DB would suppress all future scan results
                    // for any file that was ever previously detected.
                }
                
                StructuredLogger.LogInfo("History loaded successfully",
                    ("ActivityCount", _activityHistory.Count),
                    ("ThreatCount", _threatHistory.Count));
            }
        }

        public virtual void AddActivity(FileActivity activity)
        {
            lock (_historyLock)
            {
                _activityHistory.Insert(0, activity);
                if (_activityHistory.Count > MaxActivityHistory)
                    _activityHistory.RemoveAt(_activityHistory.Count - 1);
            }
            
            StructuredLogger.LogDebug("Activity added to history",
                ("FilePath", activity.FilePath),
                ("Action", activity.Action),
                ("ProcessName", activity.ProcessName),
                ("IsSuspicious", activity.IsSuspicious));
            
            _historyStore.SaveActivityAsync(activity).ContinueWith(task =>
            {
                if (task.IsFaulted && task.Exception != null)
                {
                    FileLogger.LogError(AppIdentifiers.HistoryManagerLogFile, $"Failed to save activity: {activity.FilePath}", task.Exception);
                    StructuredLogger.LogError("Failed to persist activity to store", task.Exception,
                        ("FilePath", activity.FilePath),
                        ("Action", activity.Action));
                }
            }, TaskScheduler.Default);
        }

        /// <summary>
        /// Returns true if this threat should be reported (i.e., it's new or hasn't been
        /// reported for at least <paramref name="dedupeWindowMinutes"/> minutes).
        /// Uses a sliding time window rather than a permanent block to prevent stale DB
        /// history from suppressing fresh scan detections.
        /// Enforces maximum cache size with LRU eviction.
        /// </summary>
        public virtual bool ShouldReportThreat(string path, string threatName, string action = "Detected", int dedupeWindowMinutes = 15)
        {
            // Include action in the key so that a "Quarantined" event doesn't suppress a subsequent "Detected" event
            // for the same file, which is crucial for testing the Auto Quarantine toggle.
            string threatKey = $"{path}|{threatName}|{action}";
            lock (_threatDedupLock)
            {
                if (_reportedThreats.TryGetValue(threatKey, out var lastReported))
                {
                    // Allow "MASSIVE FILE ENCRYPTION" to bypass deduplication 
                    // or if it's been more than the window.
                    if (!threatName.Contains("MASSIVE FILE ENCRYPTION") && (DateTime.Now - lastReported).TotalMinutes < dedupeWindowMinutes)
                    {
                        // Special case: If the NEW action is more "resolved" than the last one, we SHOULD allow it
                        // so that AddThreat can perform the status upgrade.
                        if (action == "Detected" || action == "Active")
                            return false; 
                    }
                }
                
                // Enforce size limit with LRU eviction before adding new entry
                if (_reportedThreats.Count >= MaxThreatCacheSize)
                {
                    var oldest = _reportedThreats
                        .OrderBy(kvp => kvp.Value)
                        .Take(_reportedThreats.Count - MaxThreatCacheSize + 1)
                        .Select(kvp => kvp.Key)
                        .ToList();
                    
                    foreach (var key in oldest)
                        _reportedThreats.Remove(key);
                }
                
                _reportedThreats[threatKey] = DateTime.Now;
                return true;
            }
        }

        public virtual void AddThreat(Threat threat)
        {
            lock (_historyLock)
            {
                // Check if we already have this threat in memory (within a recent window)
                var existing = _threatHistory.FirstOrDefault(t => 
                    string.Equals(t.Path, threat.Path, StringComparison.OrdinalIgnoreCase) && 
                    t.Name == threat.Name &&
                    (threat.Timestamp - t.Timestamp).TotalMinutes < 15);

                if (existing != null)
                {
                    // Update status if the new one is more "resolved"
                    if (threat.ActionTaken != "Detected" && threat.ActionTaken != "Active" && existing.ActionTaken != threat.ActionTaken)
                    {
                        var oldStatus = existing.ActionTaken;
                        existing.ActionTaken = threat.ActionTaken;
                        existing.Timestamp = threat.Timestamp;
                        
                        StructuredLogger.LogInfo("Threat status updated in memory",
                            ("ThreatId", existing.Id),
                            ("FilePath", threat.Path),
                            ("OldStatus", oldStatus),
                            ("NewStatus", threat.ActionTaken));
                    }
                }
                else
                {
                    _threatHistory.Insert(0, threat);
                    
                    StructuredLogger.LogWarning("New threat added to history",
                        ("ThreatId", threat.Id),
                        ("ThreatName", threat.Name),
                        ("FilePath", threat.Path),
                        ("Severity", threat.Severity.ToString()),
                        ("ProcessName", threat.ProcessName),
                        ("ActionTaken", threat.ActionTaken));
                }
            }
            _historyStore.SaveThreatAsync(threat).ContinueWith(task =>
            {
                if (task.IsFaulted && task.Exception != null)
                {
                    FileLogger.LogError(AppIdentifiers.HistoryManagerLogFile, $"Failed to save threat: {threat.Path}", task.Exception);
                    StructuredLogger.LogError("Failed to persist threat to store", task.Exception,
                        ("ThreatId", threat.Id),
                        ("FilePath", threat.Path),
                        ("ThreatName", threat.Name));
                }
            }, TaskScheduler.Default);
        }

        public virtual void UpdateThreatStatus(string path, string status)
        {
            lock (_historyLock)
            {
                // Only update the single most-recent "Detected" (unresolved) entry for this path.
                // Updating all matching threats would count duplicate alert entries as separate
                // blocked threats, inflating ThreatsBlockedCount incorrectly.
                var mostRecent = _threatHistory
                    .FirstOrDefault(t => string.Equals(t.Path, path, StringComparison.OrdinalIgnoreCase)
                                      && t.ActionTaken == "Detected");
                if (mostRecent != null)
                    mostRecent.ActionTaken = status;
            }
            _historyStore.UpdateThreatStatusAsync(path, status).ContinueWith(task =>
            {
                if (task.IsFaulted && task.Exception != null)
                {
                    FileLogger.LogError(AppIdentifiers.HistoryManagerLogFile, $"Failed to update threat status: {path}", task.Exception);
                }
            }, TaskScheduler.Default);
        }

        public virtual void UpdateThreatStatusById(string id, string status)
        {
            Threat? threat = null;
            lock (_historyLock)
            {
                threat = _threatHistory.FirstOrDefault(t => t.Id == id);
                if (threat != null)
                    threat.ActionTaken = status;
            }
            
            if (threat != null)
                _historyStore.UpdateThreatStatusAsync(threat.Path, status).ContinueWith(task =>
                {
                    if (task.IsFaulted && task.Exception != null)
                    {
                        FileLogger.LogError(AppIdentifiers.HistoryManagerLogFile, $"Failed to update threat status by ID: {id}", task.Exception);
                    }
                }, TaskScheduler.Default);
        }

        public virtual bool TryBeginMassEncryptionMitigation(string id, out Threat? threat)
        {
            threat = null;

            lock (_historyLock)
            {
                threat = _threatHistory.FirstOrDefault(t => t.Id == id);
                if (threat == null || threat.ActionTaken != "Awaiting Confirmation")
                {
                    StructuredLogger.LogWarning("Mass encryption mitigation rejected - invalid state",
                        ("ThreatId", id),
                        ("ThreatFound", threat != null),
                        ("CurrentStatus", threat?.ActionTaken ?? "N/A"));
                    return false;
                }

                threat.ActionTaken = "Mitigating";
                threat.RequiresUserConfirmation = false;
                
                StructuredLogger.LogCritical("Mass encryption mitigation started", null,
                    ("ThreatId", id),
                    ("FilePath", threat.Path),
                    ("ProcessName", threat.ProcessName),
                    ("AffectedFileCount", threat.AffectedFiles?.Count ?? 0));
            }

            var threatPath = threat.Path; // Capture for lambda
            _historyStore.UpdateThreatStatusAsync(threat.Path, "Mitigating").ContinueWith(task =>
            {
                if (task.IsFaulted && task.Exception != null)
                {
                    FileLogger.LogError(AppIdentifiers.HistoryManagerLogFile, $"Failed to update threat status to Mitigating: {threatPath}", task.Exception);
                    StructuredLogger.LogError("Failed to persist mitigation status", task.Exception,
                        ("ThreatId", id),
                        ("FilePath", threatPath));
                }
            }, TaskScheduler.Default);
            return true;
        }

        public virtual bool TryDeclineMassEncryptionThreat(string id, out Threat? threat)
        {
            threat = null;

            lock (_historyLock)
            {
                threat = _threatHistory.FirstOrDefault(t => t.Id == id);
                if (threat == null || threat.ActionTaken != "Awaiting Confirmation")
                    return false;

                threat.ActionTaken = "User Declined";
                threat.RequiresUserConfirmation = false;
            }

            var threatPath = threat.Path; // Capture for lambda
            _historyStore.UpdateThreatStatusAsync(threat.Path, "User Declined").ContinueWith(task =>
            {
                if (task.IsFaulted && task.Exception != null)
                {
                    FileLogger.LogError(AppIdentifiers.HistoryManagerLogFile, $"Failed to update threat status to User Declined: {threatPath}", task.Exception);
                }
            }, TaskScheduler.Default);
            return true;
        }

        public Threat? GetThreatById(string id)
        {
            lock (_historyLock)
            {
                return _threatHistory.FirstOrDefault(t => t.Id == id);
            }
        }

        public IEnumerable<Threat> GetRecentThreats(int count = 50)
        {
            lock (_historyLock) { return _threatHistory.Take(count).ToList(); }
        }

        public IEnumerable<FileActivity> GetRecentActivities(int count = 50)
        {
            lock (_historyLock) { return _activityHistory.Take(count).ToList(); }
        }

        /// <summary>
        /// Calculates the number of files created or renamed in the last hour.
        /// </summary>
        public int GetFilesPerHour()
        {
            lock (_historyLock)
            {
                var oneHourAgo = DateTime.Now.AddHours(-1);
                return _activityHistory.Count(a => 
                    a.Timestamp >= oneHourAgo && 
                    (a.Action == "CREATED" || a.Action.Contains("RENAMED")));
            }
        }

        public void CleanupCache()
        {
            using (StructuredLogger.BeginOperation("HistoryCacheCleanup"))
            {
                try
                {
                    lock (_threatDedupLock)
                    {
                        var now = DateTime.Now;
                        var initialCount = _reportedThreats.Count;
                        
                        // Remove old entries (older than MaxThreatCacheAgeMinutes)
                        var keysToRemove = _reportedThreats
                            .Where(kvp => (now - kvp.Value).TotalMinutes > MaxThreatCacheAgeMinutes)
                            .Select(kvp => kvp.Key)
                            .ToList();

                        foreach (var key in keysToRemove)
                            _reportedThreats.Remove(key);
                        
                        // If still too large after cleanup, remove oldest entries (LRU eviction)
                        if (_reportedThreats.Count > MaxThreatCacheSize)
                        {
                            var oldest = _reportedThreats
                                .OrderBy(kvp => kvp.Value)
                                .Take(_reportedThreats.Count - MaxThreatCacheSize)
                                .Select(kvp => kvp.Key)
                                .ToList();
                            
                            foreach (var key in oldest)
                                _reportedThreats.Remove(key);
                            
                            RansomGuard.Core.Helpers.FileLogger.Log(AppIdentifiers.HistoryManagerLogFile, $"[HistoryManager] Threat cache trimmed to {_reportedThreats.Count} entries");
                        }
                        
                        StructuredLogger.LogInfo("History cache cleanup completed",
                            ("InitialCount", initialCount),
                            ("RemovedCount", keysToRemove.Count),
                            ("FinalCount", _reportedThreats.Count));
                    }
                }
                catch (Exception ex)
                {
                    RansomGuard.Core.Helpers.FileLogger.LogError(AppIdentifiers.HistoryManagerLogFile, "[HistoryManager] CleanupCache error", ex);
                    StructuredLogger.LogError("History cache cleanup failed", ex);
                }
            }
        }

        private string GetThreatKey(Threat threat) => $"{threat.Path}|{threat.Name}";

        public async Task ClearHistory()
        {
            lock (_historyLock)
            {
                _activityHistory.Clear();
            }
            await _historyStore.ClearActivitiesAsync().ConfigureAwait(false);
            StructuredLogger.LogInfo("History Manager: Activity history cleared.");
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _activityHistory.Clear();
            _threatHistory.Clear();
            _reportedThreats.Clear();
        }
    }
}
