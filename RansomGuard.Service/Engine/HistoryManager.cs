using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using RansomGuard.Core.Models;
using RansomGuard.Service.Services;

namespace RansomGuard.Service.Engine
{
    /// <summary>
    /// Manages in-memory caching and deduplication of threats and file activities,
    /// serving as a bridge between the real-time engine and the persistent HistoryStore.
    /// </summary>
    public class HistoryManager : IDisposable
    {
        private const int MaxActivityHistory = 100;
        private const int MaxThreatCacheAgeMinutes = 60; // 1 hour (reduced from 24 hours)
        private const int MaxThreatCacheSize = 1000; // Maximum entries in dedup cache
        
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
        }

        public virtual void AddActivity(FileActivity activity)
        {
            lock (_historyLock)
            {
                _activityHistory.Insert(0, activity);
                if (_activityHistory.Count > MaxActivityHistory)
                    _activityHistory.RemoveAt(_activityHistory.Count - 1);
            }
            _ = _historyStore.SaveActivityAsync(activity);
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
                        existing.ActionTaken = threat.ActionTaken;
                        existing.Timestamp = threat.Timestamp;
                    }
                }
                else
                {
                    _threatHistory.Insert(0, threat);
                }
            }
            _ = _historyStore.SaveThreatAsync(threat);
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
            _ = _historyStore.UpdateThreatStatusAsync(path, status);
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
                _ = _historyStore.UpdateThreatStatusAsync(threat.Path, status);
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
            try
            {
                lock (_threatDedupLock)
                {
                    var now = DateTime.Now;
                    
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
                        
                        System.Diagnostics.Debug.WriteLine($"[HistoryManager] Threat cache trimmed to {_reportedThreats.Count} entries");
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[HistoryManager] CleanupCache error: {ex.Message}");
            }
        }

        private string GetThreatKey(Threat threat) => $"{threat.Path}|{threat.Name}";

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
