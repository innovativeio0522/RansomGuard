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
        private const int MaxThreatCacheAgeMinutes = 1440; // 24 hours
        
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
        /// </summary>
        public virtual bool ShouldReportThreat(string path, string threatName, int dedupeWindowMinutes = 30)
        {
            string threatKey = $"{path}|{threatName}";
            lock (_threatDedupLock)
            {
                if (_reportedThreats.TryGetValue(threatKey, out var lastReported))
                {
                    if ((DateTime.Now - lastReported).TotalMinutes < dedupeWindowMinutes)
                        return false; // Already reported within the window — suppress
                }
                _reportedThreats[threatKey] = DateTime.Now;
                return true;
            }
        }

        public virtual void AddThreat(Threat threat)
        {
            lock (_historyLock)
            {
                _threatHistory.Insert(0, threat);
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

        public IEnumerable<Threat> GetRecentThreats(int count = 50)
        {
            lock (_historyLock) { return _threatHistory.Take(count).ToList(); }
        }

        public IEnumerable<FileActivity> GetRecentActivities(int count = 50)
        {
            lock (_historyLock) { return _activityHistory.Take(count).ToList(); }
        }

        public void CleanupCache()
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
