using System;
using System.Diagnostics;
using System.IO;
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
    public class WatchdogPersistenceService(ILogger<WatchdogPersistenceService> logger) : BackgroundService
    {
        private readonly ILogger<WatchdogPersistenceService> _logger = logger;
        private const string WatchdogProcessName = "RGWorker";
        private const string WatchdogTaskName = "RGWorkerTask";
        private const int CheckIntervalMs = 5000; // Check every 5 seconds

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
            if (Process.GetProcessesByName(WatchdogProcessName).Length > 0)
            {
                return;
            }

            try
            {
                _logger.LogInformation("Watchdog missing. Triggering scheduled task to restart in user session.");
                
                string? watchdogPath = FindWatchdogPath();
                if (!string.IsNullOrEmpty(watchdogPath))
                {
                    RegisterWatchdogTask(watchdogPath);
                }
                else
                {
                    _logger.LogWarning("Watchdog executable was not found. Scheduled task will only run if it already exists.");
                }

                var psi = new ProcessStartInfo("schtasks", $"/run /tn \"{WatchdogTaskName}\"")
                {
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    WindowStyle = ProcessWindowStyle.Hidden
                };

                using var process = Process.Start(psi);
                if (process == null)
                {
                    _logger.LogWarning("Failed to start schtasks.exe while trying to run the watchdog task.");
                    return;
                }

                process.WaitForExit(3000);
                if (process.ExitCode != 0)
                {
                    _logger.LogWarning("Scheduled task run request for {taskName} exited with code {exitCode}.", WatchdogTaskName, process.ExitCode);
                }
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
                // The task is primarily invoked via `/run`; the ONCE schedule is only there to satisfy schtasks creation requirements.
                string escapedWatchdogPath = watchdogPath.Replace("\"", "\"\"");
                string args = $"/create /tn \"{WatchdogTaskName}\" /tr \"\\\"{escapedWatchdogPath}\\\"\" /sc ONCE /st 00:00 /it /f /rl HIGHEST";
                
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

        private static string? FindWatchdogPath()
        {
            string appDir = AppDomain.CurrentDomain.BaseDirectory;
            
            // 1. Check local directory (Production)
            string prodPath = Path.Combine(appDir, WatchdogProcessName + ".exe");
            if (File.Exists(prodPath)) return prodPath;

            // 2. Development Fallbacks (.artifacts output and legacy project bin folders)
            string[] fallbacks =
            [
                // CLI build output under .artifacts/bin/RansomGuard.Service/Debug/net8.0-windows/
                Path.Combine(appDir, @"..\..\..\RansomGuard.Watchdog\Debug\net8.0\RGWorker.exe"),
                Path.Combine(appDir, @"..\..\..\RansomGuard.Watchdog\Release\net8.0\RGWorker.exe"),

                // Legacy project-local output from RansomGuard.Service/bin/Debug/net8.0-windows/
                Path.Combine(appDir, @"..\..\..\..\RansomGuard.Watchdog\bin\Debug\net8.0\RGWorker.exe"),
                Path.Combine(appDir, @"..\..\..\..\RansomGuard.Watchdog\bin\Release\net8.0\RGWorker.exe")
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
