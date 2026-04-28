using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using RansomGuard.Core.Helpers;

namespace RansomGuard.Services
{
    /// <summary>
    /// Manages the lifecycle of the RansomGuard.Watchdog process.
    /// Used by App.xaml.cs (on startup) and SettingsViewModel (on toggle).
    /// </summary>
    public static class WatchdogManager
    {
        private const string WatchdogProcessName = "RGWorker";
        private const string WatchdogTaskName = "RGWorkerTask";

        /// <summary>
        /// The main entry point for engaging protection. Ensures the Sentinel Service is running
        /// and the Watchdog is active with administrative privileges.
        /// </summary>
        public static void EnsureProtectionEngaged()
        {
            try
            {
                // 1. Ensure Sentinel Service is running
                EnsureServiceRunning();

                // 2. Ensure Watchdog is running
                var existingProcesses = Process.GetProcessesByName(WatchdogProcessName);
                if (existingProcesses.Length == 0)
                {
                    LaunchWatchdog();
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[WatchdogManager] Failed to engage protection: {ex.Message}");
            }
        }

        private static void LaunchWatchdog()
        {
            // Strategy 1: Launch via app execution alias (works in MSIX — alias is in PATH)
            // The alias "RGWorker.exe" is registered in the AppxManifest and resolves
            // to the correct MSIX executable without needing a direct WindowsApps path.
            try
            {
                var psi = new ProcessStartInfo("RGWorker.exe")
                {
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    WindowStyle = ProcessWindowStyle.Hidden
                };
                Process.Start(psi);
                Debug.WriteLine("[WatchdogManager] Watchdog launched via app execution alias.");
                return;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[WatchdogManager] Alias launch failed: {ex.Message}. Trying direct path...");
            }

            // Strategy 2: Direct path fallback (works for non-MSIX / dev builds)
            string? watchdogPath = FindWatchdogPath();
            if (watchdogPath != null)
            {
                try
                {
                    var psi = new ProcessStartInfo(watchdogPath)
                    {
                        CreateNoWindow = true,
                        UseShellExecute = true,
                        WindowStyle = ProcessWindowStyle.Hidden
                    };
                    Process.Start(psi);
                    Debug.WriteLine($"[WatchdogManager] Watchdog launched via direct path: {watchdogPath}");
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[WatchdogManager] Direct path launch failed: {ex.Message}");
                }
            }
            else
            {
                Debug.WriteLine("[WatchdogManager] Watchdog executable not found.");
            }
        }

        private static void EnsureServiceRunning()
        {
            try
            {
                using (var sc = new System.ServiceProcess.ServiceController("RGServicePackaged"))
                {
                    if (sc.Status != System.ServiceProcess.ServiceControllerStatus.Running &&
                        sc.Status != System.ServiceProcess.ServiceControllerStatus.StartPending)
                    {
                        var psi = new ProcessStartInfo("cmd.exe", "/c net start RGServicePackaged")
                        {
                            Verb = "runas",
                            UseShellExecute = true,
                            CreateNoWindow = true,
                            WindowStyle = ProcessWindowStyle.Hidden
                        };
                        Process.Start(psi);
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[WatchdogManager] Service start failed: {ex.Message}");
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
            // Get the directory of the current process (reliable in MSIX)
            string? appDir = Path.GetDirectoryName(Process.GetCurrentProcess().MainModule?.FileName);
            if (string.IsNullOrEmpty(appDir)) appDir = AppDomain.CurrentDomain.BaseDirectory;

            // MSIX Fix: Check if we're in a "RansomGuard" subfolder (MSIX structure)
            // In MSIX, RGUI.exe is in "RansomGuard\" subfolder, but RGWorker.exe is in root
            if (appDir.EndsWith("RansomGuard", StringComparison.OrdinalIgnoreCase))
            {
                string? parentDir = Path.GetDirectoryName(appDir);
                if (!string.IsNullOrEmpty(parentDir))
                {
                    string msixRootPath = Path.Combine(parentDir, "RGWorker.exe");
                    if (File.Exists(msixRootPath))
                    {
                        return msixRootPath;
                    }
                }
            }

            // Standard path: same directory as UI
            string prodPath = Path.Combine(appDir, "RGWorker.exe");
            if (File.Exists(prodPath))
            {
                return prodPath;
            }

            // Fallback: check parent directory (handle other subfolder issues)
            string? parentDir2 = Path.GetDirectoryName(appDir);
            if (!string.IsNullOrEmpty(parentDir2))
            {
                string parentProdPath = Path.Combine(parentDir2, "RGWorker.exe");
                if (File.Exists(parentProdPath))
                {
                    return parentProdPath;
                }
            }

            // Development/Debug fallbacks
            string[] searchPaths =
            [
                Path.Combine(appDir, @"..\..\..\RansomGuard.Watchdog\bin\Debug\net8.0\RGWorker.exe"),
                Path.Combine(appDir, @"..\..\..\RansomGuard.Watchdog\bin\Release\net8.0\RGWorker.exe")
            ];

            foreach (var path in searchPaths)
            {
                if (File.Exists(path))
                {
                    return path;
                }
            }

            return null;
        }
    }
}
