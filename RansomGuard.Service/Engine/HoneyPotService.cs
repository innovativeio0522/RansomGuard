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

        private void CleanupBaits()
        {
            try
            {
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
