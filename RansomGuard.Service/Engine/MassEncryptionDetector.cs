using System;
using System.Collections.Generic;
using System.Linq;
using RansomGuard.Core.Models;
using RansomGuard.Core.Services;
using RansomGuard.Core.Helpers;
using RansomGuard.Core.Constants;

namespace RansomGuard.Service.Engine
{
    /// <summary>
    /// Tracks file change velocity and detects mass encryption patterns.
    /// Manages recent changes queue and triggers alerts based on heuristics.
    /// </summary>
    public class MassEncryptionDetector
    {
        private const int WindowSeconds = 10;
        private readonly Queue<FileChangeRecord> _recentChanges = new();
        private readonly object _recentChangesLock = new();

        public event Action<MassEncryptionAlert>? MassEncryptionDetected;

        /// <summary>
        /// Records a file change and checks for mass encryption patterns.
        /// </summary>
        public void RecordFileChange(string processName, int processId, string filePath, bool isSuspicious, double entropy, int sensitivityLevel)
        {
            var now = DateTime.Now;
            
            lock (_recentChangesLock)
            {
                // Track this change
                string name = (processName != "Unknown" && processName != "explorer") ? processName : "System/Unknown";
                _recentChanges.Enqueue(new FileChangeRecord
                {
                    Timestamp = now,
                    ProcessName = name,
                    ProcessId = processId,
                    FilePath = filePath,
                    IsSuspicious = isSuspicious,
                    Entropy = entropy
                });

                // Remove old changes (outside the window)
                while (_recentChanges.Count > 0 && (now - _recentChanges.Peek().Timestamp).TotalSeconds > WindowSeconds)
                    _recentChanges.Dequeue();

                // Check for mass encryption patterns
                var alert = CheckForMassEncryption(sensitivityLevel);
                if (alert != null)
                {
                    FileLogger.Log(AppIdentifiers.MassEncryptionLogFile, $"[CRITICAL] MASS ACTIVITY DETECTED! {alert.Reason}. Threshold: {alert.Threshold}. Current Total: {alert.TotalChanges}, Suspicious: {alert.SuspiciousChanges}. Culprit: {alert.ProcessName} ({alert.ProcessId})");
                    
                    MassEncryptionDetected?.Invoke(alert);
                    
                    // Clear recent changes after alert
                    _recentChanges.Clear();
                }
            }
        }

        /// <summary>
        /// Checks if current file change patterns indicate mass encryption.
        /// Returns alert details if detected, null otherwise.
        /// </summary>
        private MassEncryptionAlert? CheckForMassEncryption(int sensitivityLevel)
        {
            int totalChanges = _recentChanges.Count;
            int suspiciousChanges = 0;
            int highEntropyChanges = 0;
            
            foreach (var c in _recentChanges)
            {
                if (c.IsSuspicious) suspiciousChanges++;
                if (c.Entropy > 7.5) highEntropyChanges++;
            }
            
            int threshold = GetChangeThreshold(sensitivityLevel);

            bool isMassAlert = false;
            string alertReason = "";

            // Heuristic 1: Simple high velocity (Classic)
            if (totalChanges >= threshold)
            {
                isMassAlert = true;
                alertReason = "High volume of file modifications";
            }
            // Heuristic 2: Many suspicious changes even if below total threshold
            else if (suspiciousChanges >= (threshold / 2) && suspiciousChanges >= 3)
            {
                isMassAlert = true;
                alertReason = "Cluster of suspicious file activities";
            }
            // Heuristic 3: Multiple high-entropy writes from the same process
            else if (highEntropyChanges >= 5)
            {
                isMassAlert = true;
                alertReason = "Multiple high-entropy data writes";
            }

            if (!isMassAlert)
                return null;

            // Identify the culprit (process with most changes in this window)
            var topProcess = _recentChanges
                .GroupBy(c => new { c.ProcessName, c.ProcessId })
                .OrderByDescending(g => g.Count())
                .FirstOrDefault();

            string targetName = topProcess?.Key.ProcessName ?? "Unknown Process";
            int targetId = topProcess?.Key.ProcessId ?? 0;

            // Collect files to quarantine (deduplicate by base filename)
            var filesToQuarantine = _recentChanges
                .Where(c => !string.IsNullOrEmpty(c.FilePath))
                .GroupBy(c => {
                    string fileName = System.IO.Path.GetFileName(c.FilePath);
                    int firstDot = fileName.IndexOf('.');
                    return firstDot > 0 ? fileName.Substring(0, firstDot) : fileName;
                })
                .Select(g => g.OrderByDescending(c => c.Timestamp).First().FilePath)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            return new MassEncryptionAlert
            {
                Reason = alertReason,
                Threshold = threshold,
                TotalChanges = totalChanges,
                SuspiciousChanges = suspiciousChanges,
                HighEntropyChanges = highEntropyChanges,
                ProcessName = targetName,
                ProcessId = targetId,
                FilesToQuarantine = filesToQuarantine,
                Timestamp = DateTime.Now
            };
        }

        /// <summary>
        /// Gets additional files modified by a specific process in the recent window.
        /// Used to catch files modified after alert but before mitigation.
        /// </summary>
        public List<string> GetAdditionalFilesByProcess(int processId, string processName)
        {
            lock (_recentChangesLock)
            {
                return _recentChanges
                    .Where(c => (c.ProcessId == processId || (processId == 0 && c.ProcessName == processName)) && 
                               !string.IsNullOrEmpty(c.FilePath))
                    .Select(c => c.FilePath)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }
        }

        /// <summary>
        /// Clears the recent changes queue.
        /// </summary>
        public void ClearRecentChanges()
        {
            lock (_recentChangesLock)
            {
                _recentChanges.Clear();
            }
        }

        private static int GetChangeThreshold(int sensitivityLevel) => sensitivityLevel switch
        {
            1 => RansomGuard.Core.Configuration.AppConstants.Security.RapidModificationThreshold * 3, // Low (30)
            2 => RansomGuard.Core.Configuration.AppConstants.Security.RapidModificationThreshold * 2, // Medium (20)
            3 => RansomGuard.Core.Configuration.AppConstants.Security.RapidModificationThreshold,     // High (10)
            4 => 5,                                                                                  // Paranoid
            _ => RansomGuard.Core.Configuration.AppConstants.Security.RapidModificationThreshold
        };
    }

    /// <summary>
    /// Record of a file change for mass encryption detection.
    /// </summary>
    internal class FileChangeRecord
    {
        public DateTime Timestamp { get; set; }
        public string ProcessName { get; set; } = string.Empty;
        public int ProcessId { get; set; }
        public string FilePath { get; set; } = string.Empty;
        public bool IsSuspicious { get; set; }
        public double Entropy { get; set; }
    }

    /// <summary>
    /// Alert details for detected mass encryption activity.
    /// </summary>
    public class MassEncryptionAlert
    {
        public string Reason { get; set; } = string.Empty;
        public int Threshold { get; set; }
        public int TotalChanges { get; set; }
        public int SuspiciousChanges { get; set; }
        public int HighEntropyChanges { get; set; }
        public string ProcessName { get; set; } = string.Empty;
        public int ProcessId { get; set; }
        public List<string> FilesToQuarantine { get; set; } = new();
        public DateTime Timestamp { get; set; }
    }
}
