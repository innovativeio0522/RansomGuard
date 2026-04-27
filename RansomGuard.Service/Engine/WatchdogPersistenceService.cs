using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RansomGuard.Core.Services;

namespace RansomGuard.Service.Engine
{
    /// <summary>
    /// Ensures the RansomGuard.Watchdog process is running if enabled in settings.
    /// This completes the mutual protection loop (Service monitors Watchdog, Watchdog monitors Service).
    /// </summary>
    public class WatchdogPersistenceService : BackgroundService
    {
        private readonly ILogger<WatchdogPersistenceService> _logger;
        private const string WatchdogProcessName = "RGWorker";
        private const string WatchdogTaskName = "RGWorkerTask";
        private const int CheckIntervalMs = 5000; // Check every 5 seconds

        public WatchdogPersistenceService(ILogger<WatchdogPersistenceService> logger)
        {
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("Watchdog Persistence Service started.");

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    // Read watchdog setting from shared config (respects UI toggle)
                    var watchdogEnabled = ConfigurationService.Instance.WatchdogEnabled;
                    if (watchdogEnabled)
                    {
                        EnsureWatchdogRunning();
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error in Watchdog Persistence loop.");
                }

                await Task.Delay(CheckIntervalMs, stoppingToken);
            }
        }

        private void EnsureWatchdogRunning()
        {
            if (Process.GetProcessesByName(WatchdogProcessName).Any())
            {
                return;
            }

            try
            {
                _logger.LogInformation("Watchdog missing. Triggering scheduled task to restart in user session.");
                
                string? watchdogPath = FindWatchdogPath();
                if (watchdogPath != null)
                {
                    RegisterWatchdogTask(watchdogPath);
                }

                var psi = new ProcessStartInfo("schtasks", $"/run /tn \"{WatchdogTaskName}\"")
                {
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    WindowStyle = ProcessWindowStyle.Hidden
                };
                
                Process.Start(psi);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to trigger Watchdog task.");
            }
        }

        private void RegisterWatchdogTask(string watchdogPath)
        {
            try
            {
                // Create a task that runs as the current user and is allowed to run interactively (/IT).
                // We use /SC ONCE with a date in the past so it never triggers automatically, only via /RUN.
                string args = $"/create /tn \"{WatchdogTaskName}\" /tr \"'{watchdogPath}'\" /sc ONCE /st 00:00 /it /f /rl HIGHEST";
                
                var psi = new ProcessStartInfo("schtasks", args)
                {
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    WindowStyle = ProcessWindowStyle.Hidden
                };
                var p = Process.Start(psi);
                p?.WaitForExit(3000);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to register Watchdog task.");
            }
        }

        private string? FindWatchdogPath()
        {
            string appDir = AppDomain.CurrentDomain.BaseDirectory;
            
            // 1. Check local directory (Production)
            string prodPath = Path.Combine(appDir, WatchdogProcessName + ".exe");
            if (File.Exists(prodPath)) return prodPath;

            // 2. Development Fallbacks (Source tree)
            string[] fallbacks =
            [
                // From Service/publish/
                Path.Combine(appDir, @"..\..\..\RansomGuard.Watchdog\bin\Debug\net9.0\RansomGuard.Watchdog.exe"),
                // From Service/bin/Debug/net8.0/
                Path.Combine(appDir, @"..\..\..\..\RansomGuard.Watchdog\bin\Debug\net9.0\RansomGuard.Watchdog.exe")
            ];

            foreach (var path in fallbacks)
            {
                try
                {
                    string fullPath = Path.GetFullPath(path);
                    if (File.Exists(fullPath)) return fullPath;
                }
                catch { }
            }

            return null;
        }
    }
}
