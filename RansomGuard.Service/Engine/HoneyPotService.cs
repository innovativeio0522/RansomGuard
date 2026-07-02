using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using RansomGuard.Core.Services;
using RansomGuard.Core.Helpers;
using RansomGuard.Core.Models;
using RansomGuard.Core.Constants;
using RansomGuard.Core.Configuration;

namespace RansomGuard.Service.Engine
{
    public class HoneyPotService : IDisposable
    {
        private readonly SentinelEngine _engine;
        private readonly List<FileSystemWatcher> _baitWatchers = new();
        private const string BaitFolderName = AppIdentifiers.HoneypotMarker;
        private const string BaitFileName = AppIdentifiers.HoneypotFileName;

        public HoneyPotService(SentinelEngine engine)
        {
            _engine = engine;
        }

        public void Start()
        {
            DeployBaits();
        }

        public void Stop()
        {
            CleanupBaits();
        }

        private void DeployBaits()
        {
            var targets = GetDefaultBaitLocations();
            FileLogger.Log(AppIdentifiers.SentinelEngineLogFile, $"[HoneyPot] Deploying baits to {targets.Count} locations...");
            foreach (var path in targets)
            {
                FileLogger.Log(AppIdentifiers.SentinelEngineLogFile, $"[HoneyPot] Processing location: {path}");
                if (Directory.Exists(path))
                {
                    var baitPath = Path.Combine(path, BaitFolderName);
                    try
                    {
                        if (!Directory.Exists(baitPath))
                        {
                            var di = Directory.CreateDirectory(baitPath);
                            di.Attributes = FileAttributes.Directory | FileAttributes.Hidden | FileAttributes.System;
                        }

                        var filePath = Path.Combine(baitPath, BaitFileName);
                        if (!File.Exists(filePath))
                        {
                            // Use synchronous file I/O to avoid threadpool deadlock
                            File.WriteAllText(filePath, "This is a Sentinel protection file. DO NOT DELETE.");
                            File.SetAttributes(filePath, FileAttributes.Hidden | FileAttributes.System);
                        }

                        var watcher = new FileSystemWatcher(baitPath)
                        {
                            NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Security,
                            EnableRaisingEvents = true
                        };

                        watcher.Changed += (s, e) => HandleBaitHit(e.FullPath);
                        watcher.Deleted += (s, e) => HandleBaitHit(e.FullPath);
                        watcher.Renamed += (s, e) => HandleBaitHit(e.FullPath);

                        _baitWatchers.Add(watcher);
                    }
                    catch (Exception ex)
                    {
                        // Some systems employ Controlled Folder Access (Windows Defender) 
                        // which blocks hidden folder creation in Documents/Desktop. We degrade gracefully.
                        FileLogger.LogWarning(AppIdentifiers.SentinelEngineLogFile, $"[HoneyPot] Warning: OS blocked deployment at {path}: {ex.Message}");
                    }
                }
            }
        }

        private void HandleBaitHit(string path)
        {
            try
            {
                _engine.ReportThreat(path, "HONEY POT TRIPWIRE TRIGGERED", 
                    "An unauthorized process attempted to access or modify a hidden Sentinel bait file.", 
                    "Unknown", 0, ThreatSeverity.High);
            }
            catch (Exception ex)
            {
                FileLogger.LogError(AppIdentifiers.SentinelEngineLogFile, "[HoneyPot] Error reporting bait hit", ex);
            }
        }

        private void CleanupBaits()
        {
            try
            {
                foreach (var watcher in _baitWatchers)
                {
                    try
                    {
                        watcher.EnableRaisingEvents = false;
                        watcher.Dispose();
                    }
                    catch { }
                }
                _baitWatchers.Clear();

                var targets = GetDefaultBaitLocations();
                foreach (var path in targets)
                {
                    var baitPath = Path.Combine(path, BaitFolderName);
                    try
                    {
                        if (Directory.Exists(baitPath))
                        {
                            Directory.Delete(baitPath, true);
                        }
                    }
                    catch { }
                }
            }
            catch (Exception ex)
            {
                FileLogger.LogError(AppIdentifiers.SentinelEngineLogFile, "[HoneyPot] Error during bait cleanup", ex);
            }
        }

        private List<string> GetDefaultBaitLocations()
        {
            var locations = new List<string>
            {
                PathConfiguration.HoneyPotPath, // Global guaranteed location
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) + "\\Downloads"
            };

            // Add user monitored paths
            locations.AddRange(ConfigurationService.Instance.MonitoredPaths);

            return locations.Distinct().ToList();
        }

        public void Dispose()
        {
            Stop();
        }
    }
}
