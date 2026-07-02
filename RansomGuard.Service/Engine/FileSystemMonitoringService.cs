using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using RansomGuard.Core.Services;
using RansomGuard.Core.Helpers;
using RansomGuard.Core.Constants;

namespace RansomGuard.Service.Engine
{
    /// <summary>
    /// Manages FileSystemWatcher instances for monitored directories.
    /// Handles path configuration changes, debouncing, and event normalization.
    /// </summary>
    public class FileSystemMonitoringService : IDisposable
    {
        private readonly List<FileSystemWatcher> _watchers = new();
        private readonly IEntropyAnalyzer _entropyAnalyzer;
        private bool _disposed;

        public event Action<FileSystemEvent>? FileEventDetected;

        public FileSystemMonitoringService(IEntropyAnalyzer entropyAnalyzer)
        {
            _entropyAnalyzer = entropyAnalyzer ?? throw new ArgumentNullException(nameof(entropyAnalyzer));
        }

        /// <summary>
        /// Initializes or re-initializes all FileSystemWatcher instances based on current configuration.
        /// </summary>
        public void InitializeWatchers(bool realTimeProtectionEnabled, IEnumerable<string> standardPaths, IEnumerable<string> customPaths)
        {
            lock (_watchers)
            {
                // Dispose existing watchers
                foreach (var w in _watchers)
                {
                    w.EnableRaisingEvents = false;
                    w.Dispose();
                }
                _watchers.Clear();

                FileLogger.Log(AppIdentifiers.FileMonitoringLogFile, $"[FileSystemMonitoring] RealTimeProtection={realTimeProtectionEnabled}");
                if (!realTimeProtectionEnabled) return;

                // Merge and deduplicate paths
                var allPaths = standardPaths
                    .Concat(customPaths)
                    .Select(p => Path.GetFullPath(p).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

                FileLogger.Log(AppIdentifiers.FileMonitoringLogFile, $"[FileSystemMonitoring] Total paths to watch: {allPaths.Count}");

                foreach (var rawPath in allPaths)
                {
                    string path = rawPath;
                    
                    // Force uppercase drive letter - critical for FileSystemWatcher reliability on Windows
                    if (path.Length >= 2 && path[1] == ':' && char.IsLower(path[0]))
                    {
                        path = char.ToUpper(path[0]) + path.Substring(1);
                    }

                    if (!Directory.Exists(path))
                    {
                        FileLogger.LogWarning(AppIdentifiers.FileMonitoringLogFile, $"[FileSystemMonitoring] Path does not exist: {path}");
                        continue;
                    }

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
                        FileLogger.Log(AppIdentifiers.FileMonitoringLogFile, $"[FileSystemMonitoring] Watcher ACTIVE for: {path}");
                        watcher = null; // Ownership transferred - don't dispose in catch block
                    }
                    catch (Exception ex)
                    {
                        FileLogger.LogError(AppIdentifiers.FileMonitoringLogFile, $"[FileSystemMonitoring] Failed to create watcher for {path}: {ex.Message}");
                        watcher?.Dispose();
                    }
                }
            }
        }

        private void OnFileChanged(string path, string action)
        {
            try
            {
                // Emit normalized event
                var fsEvent = new FileSystemEvent
                {
                    Path = path,
                    Action = action,
                    Timestamp = DateTime.Now,
                    IsSuspicious = _entropyAnalyzer.IsSuspiciousExtension(path) || 
                                  path.Contains("!$RansomGuard_Bait", StringComparison.OrdinalIgnoreCase)
                };

                FileEventDetected?.Invoke(fsEvent);
            }
            catch (Exception ex)
            {
                FileLogger.LogError(AppIdentifiers.FileMonitoringLogFile, $"[FileSystemMonitoring] Error processing event for {path}: {ex.Message}");
            }
        }

        public int GetActiveWatcherCount()
        {
            lock (_watchers)
            {
                return _watchers.Count(w => w.EnableRaisingEvents);
            }
        }

        public int GetTotalWatcherCount()
        {
            lock (_watchers)
            {
                return _watchers.Count;
            }
        }

        public IEnumerable<string> GetMonitoredPaths()
        {
            lock (_watchers)
            {
                return _watchers.Select(w => w.Path).ToList();
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            lock (_watchers)
            {
                foreach (var w in _watchers)
                {
                    try
                    {
                        w.EnableRaisingEvents = false;
                        w.Dispose();
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"[FileSystemMonitoring] Error disposing watcher: {ex.Message}");
                    }
                }
                _watchers.Clear();
            }
        }
    }

    /// <summary>
    /// Represents a normalized file system event.
    /// </summary>
    public class FileSystemEvent
    {
        public string Path { get; set; } = string.Empty;
        public string Action { get; set; } = string.Empty;
        public DateTime Timestamp { get; set; }
        public bool IsSuspicious { get; set; }
    }
}
