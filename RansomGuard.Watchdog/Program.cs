using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.ServiceProcess;
using System.Text.Json;
using System.Threading;
using RansomGuard.Core.Helpers;

namespace RansomGuard.Watchdog
{
    partial class Program
    {
        [LibraryImport("kernel32.dll")]
        private static partial IntPtr GetConsoleWindow();

        [LibraryImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static partial bool ShowWindow(IntPtr hWnd, int nCmdShow);

        const int SW_HIDE = 0;
        const string ServiceName = "RGService";
        private static readonly string ConfigPath = Path.Combine(
            PathConfiguration.GetConfigDirectory(),
            "config.json");

        static void Main(string[] args)
        {
            // Stealth mode: Hide the console window
            var handle = GetConsoleWindow();
            ShowWindow(handle, SW_HIDE);

            LogToFile("=== RansomGuard Watchdog Starting ===");
            LogToFile($"Watchdog started at: {DateTime.Now}");
            LogToFile($"Base directory: {AppDomain.CurrentDomain.BaseDirectory}");
            LogToFile($"Config path: {ConfigPath}");

            while (true)
            {
                try
                {
                    // Check if user has disabled the Watchdog via Settings — exit if so.
                    if (!IsWatchdogEnabled())
                    {
                        LogToFile("Watchdog disabled by user. Exiting.");
                        Environment.Exit(0);
                        return;
                    }

#pragma warning disable CA1416 // Validate platform compatibility - RansomGuard is Windows-only
                    CheckServiceStatus();
#pragma warning restore CA1416
                    CheckUIStatus();
                }
                catch (Exception ex)
                {
                    LogToFile($"[Watchdog] Main loop error: {ex.Message}");
                    LogToFile($"[Watchdog] Stack trace: {ex.StackTrace}");
                }

                Thread.Sleep(5000); // Check every 5 seconds
            }
        }

        static void LogToFile(string message)
        {
            try
            {
                string logDir = Path.Combine(PathConfiguration.GetConfigDirectory(), "Logs");
                if (!Directory.Exists(logDir)) Directory.CreateDirectory(logDir);
                string logPath = Path.Combine(logDir, "watchdog.log");
                File.AppendAllText(logPath, $"{DateTime.Now}: {message}\n");
            }
            catch { }
            Debug.WriteLine(message);
        }

        /// <summary>
        /// Reads WatchdogEnabled from config.json. Defaults to true if file is missing or unreadable.
        /// </summary>
        static bool IsWatchdogEnabled()
        {
            try
            {
                if (!File.Exists(ConfigPath)) return true;
                string json = File.ReadAllText(ConfigPath);
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("WatchdogEnabled", out var prop))
                    return prop.GetBoolean();
                return true; // Default: enabled
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Watchdog] IsWatchdogEnabled failed: {ex.Message}");
                return true;
            }
        }

        static void CheckUIStatus()
        {
            var processes = Process.GetProcessesByName("RGUI");
            if (processes.Length == 0)
            {
                try
                {
                    string appDir = AppDomain.CurrentDomain.BaseDirectory;
                    string appPath = Path.Combine(appDir, "RGUI.exe");

                    LogToFile($"[Watchdog] UI not running. Searching for RGUI.exe in: {appDir}");

                    // Fallback to subfolder
                    if (!File.Exists(appPath))
                    {
                        string subPath = Path.Combine(appDir, "RansomGuard", "RGUI.exe");
                        LogToFile($"[Watchdog] Checking subfolder: {subPath}");
                        if (File.Exists(subPath)) appPath = subPath;
                    }

                    // Fallback to parent folder
                    if (!File.Exists(appPath))
                    {
                        string? parentDir = Path.GetDirectoryName(appDir.TrimEnd(Path.DirectorySeparatorChar));
                        if (parentDir != null)
                        {
                            string parentPath = Path.Combine(parentDir, "RGUI.exe");
                            LogToFile($"[Watchdog] Checking parent folder: {parentPath}");
                            if (File.Exists(parentPath)) appPath = parentPath;
                        }
                    }

                    // Development fallback paths
                    if (!File.Exists(appPath))
                    {
                        string[] devPaths = new[]
                        {
                            Path.GetFullPath(Path.Combine(appDir, @"..\..\..\RansomGuard\Debug\net8.0-windows\RGUI.exe")),
                            Path.GetFullPath(Path.Combine(appDir, @"..\..\..\RansomGuard\Release\net8.0-windows\RGUI.exe")),
                            Path.GetFullPath(Path.Combine(appDir, @"..\..\..\..\bin\Debug\net8.0-windows\RGUI.exe")),
                            Path.GetFullPath(Path.Combine(appDir, @"..\..\..\bin\Debug\net8.0-windows\RGUI.exe")),
                            Path.GetFullPath(Path.Combine(appDir, @"..\..\bin\Debug\net8.0-windows\RGUI.exe")),
                            Path.GetFullPath(Path.Combine(appDir, @"..\..\..\..\bin\Release\net8.0-windows\RGUI.exe")),
                            Path.GetFullPath(Path.Combine(appDir, @"..\..\..\bin\Release\net8.0-windows\RGUI.exe"))
                        };

                        foreach (var devPath in devPaths)
                        {
                            if (File.Exists(devPath))
                            {
                                appPath = devPath;
                                LogToFile($"[Watchdog] Found UI at development path: {appPath}");
                                break;
                            }
                        }
                    }
                    else
                    {
                        LogToFile($"[Watchdog] Found UI at production path: {appPath}");
                    }

                    if (File.Exists(appPath))
                    {
                        LogToFile($"[Watchdog] UI not running. Found at: {appPath}");
                        
                        // Try launching via Execution Alias first (more robust for MSIX)
                        // Using 'cmd /c start' can sometimes help bypass integrity level issues
                        try 
                        {
                            LogToFile("[Watchdog] Attempting restart via Alias: RGUI.exe");
                            ProcessStartInfo psiAlias = new("cmd.exe", "/c start RGUI.exe --startup")
                            {
                                CreateNoWindow = true,
                                UseShellExecute = true
                            };
                            Process.Start(psiAlias);
                            LogToFile("[Watchdog] Alias launch command sent.");
                            
                            // Give it a moment to start
                            Thread.Sleep(2000);
                            if (Process.GetProcessesByName("RGUI").Any())
                            {
                                LogToFile("[Watchdog] UI successfully restarted via Alias.");
                                return;
                            }
                        }
                        catch (Exception aliasEx)
                        {
                            LogToFile($"[Watchdog] Alias launch failed: {aliasEx.Message}");
                        }

                        // Fallback to direct EXE with ShellExecute
                        try
                        {
                            LogToFile($"[Watchdog] Attempting restart via direct EXE: {appPath}");
                            ProcessStartInfo psi = new(appPath, "--startup")
                            {
                                UseShellExecute = true,
                                WorkingDirectory = Path.GetDirectoryName(appPath)
                            };
                            var process = Process.Start(psi);
                            if (process != null)
                            {
                                LogToFile($"[Watchdog] UI restarted via direct EXE (PID: {process.Id})");
                            }
                        }
                        catch (Exception exeEx)
                        {
                            LogToFile($"[Watchdog] Direct EXE launch failed: {exeEx.Message}");
                        }
                    }
                    else
                    {
                        LogToFile($"[Watchdog] RGUI.exe not found. Searched in: {appDir}");
                    }
                }
                catch (Exception ex)
                {
                    LogToFile($"[Watchdog] UI restart error: {ex.Message}");
                    LogToFile($"[Watchdog] Stack trace: {ex.StackTrace}");
                }
            }
        }

        [SupportedOSPlatform("windows")]
        static void CheckServiceStatus()
        {
            try
            {
#pragma warning disable CA1416 // Validate platform compatibility - RansomGuard is Windows-only
                using ServiceController sc = new(ServiceName);
                try
                {
                    LogToFile($"[Watchdog] Checking service '{ServiceName}' status: {sc.Status}");
                    
                    if (sc.Status == ServiceControllerStatus.Stopped || sc.Status == ServiceControllerStatus.StopPending)
                    {
                        LogToFile($"[Watchdog] Service is {sc.Status}. Attempting to start...");
                        Thread.Sleep(1000);
                        sc.Refresh();

                        if (sc.Status == ServiceControllerStatus.Stopped)
                        {
                            LogToFile("[Watchdog] Starting service...");
                            sc.Start();
                            sc.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(10));
                            LogToFile("[Watchdog] Service started successfully");
                        }
                    }
                }
                catch (InvalidOperationException ex)
                {
                    LogToFile($"[Watchdog] Service '{ServiceName}' not accessible: {ex.Message}");
                }
                catch (System.ComponentModel.Win32Exception ex)
                {
                    // Access denied - expected when running without elevation in MSIX
                    // The service is auto-start via SCM, so it will recover on its own
#pragma warning restore CA1416
                    LogToFile($"[Watchdog] Cannot control service (no admin rights, SCM will handle): {ex.Message}");
                }
                catch (System.ServiceProcess.TimeoutException ex)
                {
                    LogToFile($"[Watchdog] Service start timeout: {ex.Message}");
                }
            }
            catch (Exception ex)
            {
                LogToFile($"[Watchdog] Service check error: {ex.Message}");
            }
        }
    }
}
