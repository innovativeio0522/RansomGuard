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
        private const string WatchdogProcessName = "RansomGuard.Watchdog";

        /// <summary>
        /// Spawns the Watchdog process if it is not already running.
        /// </summary>
        public static void EnsureWatchdogRunning()
        {
            try
            {
                if (Process.GetProcessesByName(WatchdogProcessName).Any()) return;

                string watchdogPath = FindWatchdogPath();
                if (watchdogPath == null) return;

                var psi = new ProcessStartInfo(watchdogPath)
                {
                    UseShellExecute = true,
                    WindowStyle = ProcessWindowStyle.Hidden
                };
                Process.Start(psi);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[WatchdogManager] Failed to start Watchdog: {ex.Message}");
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
            string prodPath = Path.Combine(appDir, "RansomGuard.Watchdog.exe");
            if (File.Exists(prodPath)) return prodPath;

            // Development: Try various depths to find the solution root and then the watchdog project
            string[] searchPaths = new[]
            {
                Path.Combine(appDir, @"..\..\..\RansomGuard.Watchdog\bin\Debug\net9.0\RansomGuard.Watchdog.exe"),
                Path.Combine(appDir, @"..\..\..\..\RansomGuard.Watchdog\bin\Debug\net9.0\RansomGuard.Watchdog.exe"),
                @"f:\Github Projects\RansomGuard\RansomGuard.Watchdog\bin\Debug\net9.0\RansomGuard.Watchdog.exe"
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
