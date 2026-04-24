using System;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace RansomGuard.Services
{
    /// <summary>
    /// Manages the lifecycle of the RansomGuard.Watchdog process.
    /// Used by App.xaml.cs (on startup) and SettingsViewModel (on toggle).
    /// </summary>
    public static class WatchdogManager
    {
        private const string WatchdogProcessName = "MaintenanceWorker";
        private const string WatchdogTaskName = "WinMaintenanceWorkerTask";

        /// <summary>
        /// Spawns the Watchdog process if it is not already running.
        /// Uses schtasks to ensure the process runs in the interactive user session even if triggered by a service.
        /// </summary>
        public static void EnsureWatchdogRunning()
        {
            try
            {
                if (Process.GetProcessesByName(WatchdogProcessName).Any()) return;

                string? watchdogPath = FindWatchdogPath();
                if (watchdogPath == null) return;

                // 1. Register the task (or update it) to ensure it points to the correct path
                RegisterWatchdogTask(watchdogPath);

                // 2. Run the task
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
                Debug.WriteLine($"[WatchdogManager] Failed to start Watchdog via task: {ex.Message}");
            }
        }

        private static void RegisterWatchdogTask(string watchdogPath)
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
                Debug.WriteLine($"[WatchdogManager] Failed to register Watchdog task: {ex.Message}");
            }
        }

        /// <summary>
        /// Kills all running Watchdog processes.
        /// </summary>
        public static void KillWatchdog()
        {
            try
            {
                foreach (var p in Process.GetProcessesByName(WatchdogProcessName))
                {
                    p.Kill();
                    p.WaitForExit(3000);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[WatchdogManager] Failed to kill Watchdog: {ex.Message}");
            }
        }

        /// <summary>
        /// Finds the Watchdog executable path — checks production dir first, then dev fallback.
        /// </summary>
        private static string? FindWatchdogPath()
        {
            // Production: same folder as the UI exe
            string appDir = AppDomain.CurrentDomain.BaseDirectory;
            string prodPath = Path.Combine(appDir, "MaintenanceWorker.exe");
            if (File.Exists(prodPath)) return prodPath;

            // Development: Try various depths to find the solution root and then the watchdog project
            string[] searchPaths = new[]
            {
                Path.Combine(appDir, @"..\..\..\RansomGuard.Watchdog\bin\Debug\net9.0\MaintenanceWorker.exe"),
                Path.Combine(appDir, @"..\..\..\..\RansomGuard.Watchdog\bin\Debug\net9.0\MaintenanceWorker.exe"),
                @"f:\Github Projects\RansomGuard\RansomGuard.Watchdog\bin\Debug\net9.0\MaintenanceWorker.exe"
            };

            foreach (var path in searchPaths)
            {
                try {
                    string fullPath = Path.GetFullPath(path);
                    if (File.Exists(fullPath)) return fullPath;
                } catch { }
            }

            return null;
        }
    }
}
