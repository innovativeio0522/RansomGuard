using System;
using System.Collections.Concurrent;
using System.Diagnostics;

namespace RansomGuard.Core.Helpers
{
    /// <summary>
    /// Provides accurate per-process CPU usage tracking by calculating deltas in ProcessorTime.
    /// </summary>
    public class ProcessStatsProvider
    {
        private static readonly Lazy<ProcessStatsProvider> _instance = 
            new Lazy<ProcessStatsProvider>(() => new ProcessStatsProvider());
        
        public static ProcessStatsProvider Instance => _instance.Value;

        private readonly ConcurrentDictionary<int, (TimeSpan processorTime, DateTime timestamp)> _statsMap = new();
        private readonly int _processorCount = Environment.ProcessorCount;

        private ProcessStatsProvider() { }

        /// <summary>
        /// Calculates the CPU usage percentage for a process since the last poll.
        /// </summary>
        public double GetCpuUsage(Process process)
        {
            try
            {
                var now = DateTime.UtcNow;
                var currentTotalTime = process.TotalProcessorTime;
                int pid = process.Id;

                if (_statsMap.TryGetValue(pid, out var lastStats))
                {
                    var timeDiff = now - lastStats.timestamp;
                    var processorDiff = currentTotalTime - lastStats.processorTime;

                    if (timeDiff.TotalMilliseconds <= 0) return 0;

                    // Calculate percentage: (DeltaTime / WallClockTime) / Cores * 100
                    double usage = (processorDiff.TotalMilliseconds / timeDiff.TotalMilliseconds) / _processorCount * 100.0;
                    
                    // Update entry
                    _statsMap[pid] = (currentTotalTime, now);
                    
                    return Math.Round(Math.Clamp(usage, 0, 100), 1);
                }
                else
                {
                    // First poll: record initial state and return 0
                    _statsMap[pid] = (currentTotalTime, now);
                    return 0;
                }
            }
            catch (Exception)
            {
                // Likely access denied for certain system processes
                return 0;
            }
        }

        /// <summary>
        /// Cleans up stats for processes that are no longer running.
        /// </summary>
        public void Cleanup()
        {
            var pids = Process.GetProcesses();
            var activePids = new System.Collections.Generic.HashSet<int>(System.Linq.Enumerable.Select(pids, p => p.Id));
            
            foreach (var key in _statsMap.Keys)
            {
                if (!activePids.Contains(key))
                {
                    _statsMap.TryRemove(key, out _);
                }
            }
        }
    }
}
