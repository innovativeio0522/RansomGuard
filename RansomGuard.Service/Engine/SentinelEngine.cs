using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Diagnostics;
using System.Timers;
using RansomGuard.Core.Models;
using RansomGuard.Core.Interfaces;
using RansomGuard.Core.Services;

namespace RansomGuard.Service.Engine
{
    public class SentinelEngine : ISystemMonitorService
    {
        public bool IsConnected => true;
        public event Action<bool>? ConnectionStatusChanged = delegate { };
        public event Action<FileActivity>? FileActivityDetected;
        public event Action<Threat>? ThreatDetected;

        private readonly List<FileSystemWatcher> _watchers = new();
        private readonly List<FileActivity> _activityHistory = new();
        private readonly List<Threat> _threatHistory = new();
        private readonly object _historyLock = new object();
        private readonly Queue<DateTime> _recentChanges = new();
        private const int ChangeThreshold = 15; 
        private const int WindowSeconds = 5;

        public bool IsHoneyPotActive { get; set; }
        public bool IsVssShieldActive { get; set; }
        public bool IsPanicModeActive { get; set; }

        private PerformanceCounter? _cpuCounter;
        private DateTime _lastScanTime = DateTime.Now.AddDays(-1);
        private System.Timers.Timer? _telemetryTimer;

        private double _currentCpuUsage = 0;
        private long _currentMemoryUsage = 0;

        public SentinelEngine()
        {
            InitializeCounters();
            InitializeWatchers();
            StartTelemetryPolling();
            
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
                
                try {
                    _currentMemoryUsage = Process.GetCurrentProcess().WorkingSet64;
                } catch { }
            };
            _telemetryTimer.Start();
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
            var activity = new FileActivity
            {
                Timestamp = DateTime.Now,
                Action = action,
                FilePath = path,
                ProcessName = "System",
                IsSuspicious = CheckSuspiciousExtension(path) || CheckSuspiciousPattern(path, action)
            };

            lock (_historyLock)
            {
                _activityHistory.Insert(0, activity);
                if (_activityHistory.Count > 100) _activityHistory.RemoveAt(100);
            }

            FileActivityDetected?.Invoke(activity);

            if (activity.IsSuspicious)
            {
                ReportThreat(path, "Suspicious Activity Detected");
            }
            
            // BEHAVIORAL ANALYSIS: Check for Mass Encryption Velocity
            CheckMassChangeVelocity();
        }

        private void CheckMassChangeVelocity()
        {
            var now = DateTime.Now;
            lock (_recentChanges)
            {
                _recentChanges.Enqueue(now);
                while (_recentChanges.Count > 0 && (now - _recentChanges.Peek()).TotalSeconds > WindowSeconds)
                {
                    _recentChanges.Dequeue();
                }

                if (_recentChanges.Count >= ChangeThreshold)
                {
                    ReportThreat("ALL_DRIVES", "MASSIVE FILE ENCRYPTION ACTION DETECTED", ThreatSeverity.Critical);
                    _recentChanges.Clear(); // Prevent multiple triggers for same event
                }
            }
        }

        public void ReportThreat(string path, string threatName, ThreatSeverity severity = ThreatSeverity.Medium)
        {
            var threat = new Threat
            {
                Name = threatName,
                Path = path,
                ProcessName = "Sentinel Heuristics",
                Severity = severity,
                Timestamp = DateTime.Now
            };

            lock (_historyLock)
            {
                if (!_threatHistory.Any(t => t.Path == path && t.Name == threatName))
                {
                    _threatHistory.Insert(0, threat);
                }
                else return; 
            }
            ThreatDetected?.Invoke(threat);
        }

        private bool CheckSuspiciousExtension(string path)
        {
            string ext = Path.GetExtension(path).ToLower();
            string[] blocked = { ".locked", ".encrypted", ".crypty", ".wannacry", ".locky" };
            return blocked.Contains(ext);
        }

        private bool CheckSuspiciousPattern(string path, string action)
        {
            return action.Contains("RENAMED") && action.ToLower().Contains(".locked");
        }

        public IEnumerable<Threat> GetRecentThreats() => _threatHistory.Take(50).ToList();
        public IEnumerable<FileActivity> GetRecentFileActivities() => _activityHistory.Take(50).ToList();
        
        public DateTime GetLastScanTime() => _lastScanTime;

        public async Task PerformQuickScan()
        {
            await Task.Run(() => {
                var paths = ConfigurationService.Instance.MonitoredPaths;
                foreach (var path in paths)
                {
                    if (Directory.Exists(path))
                    {
                        try
                        {
                            var files = Directory.EnumerateFiles(path, "*.*", SearchOption.AllDirectories);
                            foreach (var file in files)
                            {
                                if (CheckSuspiciousExtension(file))
                                {
                                    ReportThreat(file, "Existing Ransomware Artifact Found");
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
            });
        }

        public IEnumerable<ProcessInfo> GetActiveProcesses()
        {
            return Process.GetProcesses().Select(p => {
                try {
                    return new ProcessInfo { 
                        Pid = p.Id, 
                        Name = p.ProcessName, 
                        CpuUsage = 0,
                        MemoryUsage = p.WorkingSet64 
                    };
                } catch { return null; }
            }).Where(p => p != null).Cast<ProcessInfo>().OrderByDescending(p => p.MemoryUsage).Take(20).ToList();
        }

        public double GetSystemCpuUsage() => _currentCpuUsage;
        public long GetSystemMemoryUsage() => _currentMemoryUsage;
        public int GetMonitoredFilesCount() => _watchers.Count;

        public RansomGuard.Core.IPC.TelemetryData GetTelemetry()
        {
            return new RansomGuard.Core.IPC.TelemetryData
            {
                CpuUsage = _currentCpuUsage,
                MemoryUsage = _currentMemoryUsage,
                MonitoredFilesCount = _watchers.Count,
                ProcessesCount = Process.GetProcesses().Length,
                IsHoneyPotActive = IsHoneyPotActive,
                IsVssShieldActive = IsVssShieldActive,
                IsPanicModeActive = IsPanicModeActive,
                QuarantinedFilesCount = GetQuarantinedFiles().Count(),
                QuarantineStorageMb = GetQuarantineStorageUsage()
            };
        }

        public IEnumerable<string> GetQuarantinedFiles()
        {
            const string quarantinePath = @"C:\RansomGuard\Quarantine";
            if (!Directory.Exists(quarantinePath)) return Enumerable.Empty<string>();
            return Directory.EnumerateFiles(quarantinePath, "*.quarantine");
        }

        public double GetQuarantineStorageUsage()
        {
            const string quarantinePath = @"C:\RansomGuard\Quarantine";
            if (!Directory.Exists(quarantinePath)) return 0;
            
            var files = new DirectoryInfo(quarantinePath).GetFiles("*.quarantine");
            long totalBytes = files.Sum(f => f.Length);
            return totalBytes / (1024.0 * 1024.0); // Return in MB
        }

        public async Task KillProcess(int pid)
        {
            await Task.Run(() => {
                try
                {
                    var p = Process.GetProcessById(pid);
                    p.Kill(true);
                }
                catch { }
            });
        }
    }
}
