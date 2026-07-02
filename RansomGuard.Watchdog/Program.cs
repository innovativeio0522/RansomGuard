using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.ServiceProcess;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
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
        private const string ServiceName = RansomGuard.Core.Constants.AppIdentifiers.ServiceName;
        private static readonly string ConfigPath = Path.Combine(
            PathConfiguration.GetConfigDirectory(),
            RansomGuard.Core.Constants.AppIdentifiers.ConfigFileName);

        private static int _serviceFailureCount = 0;
        private static int _uiFailureCount = 0;
        private const int MaxBackoffMinutes = 30;

        static async Task Main(string[] args)
        {
            // Stealth mode: Hide the console window
            var handle = GetConsoleWindow();
            ShowWindow(handle, SW_HIDE);

            LogToFile("=== RansomGuard Watchdog Starting ===");
            LogToFile($"Watchdog started at: {DateTime.Now}");
            LogToFile($"Base directory: {AppDomain.CurrentDomain.BaseDirectory}");
            LogToFile($"Config path: {ConfigPath}");

            // Create cancellation token for graceful shutdown
            using var cts = new CancellationTokenSource();
            
            // Handle Ctrl+C for graceful shutdown
            Console.CancelKeyPress += (s, e) =>
            {
                e.Cancel = true;
                cts.Cancel();
            };

            try
            {
                await RunWatchdogLoopAsync(cts.Token);
            }
            catch (OperationCanceledException)
            {
                LogToFile("[Watchdog] Shutdown requested");
            }
            catch (Exception ex)
            {
                LogToFile($"[Watchdog] Fatal error: {ex.Message}");
                LogToFile($"[Watchdog] Stack trace: {ex.StackTrace}");
            }
        }

        static async Task RunWatchdogLoopAsync(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
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
                    await CheckServiceStatusWithBackoffAsync(cancellationToken);
#pragma warning restore CA1416
                    await CheckUIStatusWithBackoffAsync(cancellationToken);
                }
                catch (Exception ex)
                {
                    LogToFile($"[Watchdog] Main loop error: {ex.Message}");
                    LogToFile($"[Watchdog] Stack trace: {ex.StackTrace}");
                }

                // Base wait asynchronously for 5 seconds before next check
                await Task.Delay(5000, cancellationToken);
            }
        }

        [SupportedOSPlatform("windows")]
        static async Task CheckServiceStatusWithBackoffAsync(CancellationToken cancellationToken)
        {
            if (_serviceFailureCount > 0)
            {
                int delayMs = (int)Math.Min(Math.Pow(2, _serviceFailureCount) * 1000, MaxBackoffMinutes * 60 * 1000);
                LogToFile($"[Watchdog] Service in backoff. Waiting {delayMs/1000}s before next attempt.");
                await Task.Delay(delayMs, cancellationToken);
            }

            bool wasStarted = await CheckServiceStatusAsync(cancellationToken);
            if (wasStarted)
            {
                _serviceFailureCount = 0; // Reset on success
            }
            else
            {
                _serviceFailureCount++;
            }
        }

        static async Task CheckUIStatusWithBackoffAsync(CancellationToken cancellationToken)
        {
            if (_uiFailureCount > 0)
            {
                int delayMs = (int)Math.Min(Math.Pow(2, _uiFailureCount) * 1000, MaxBackoffMinutes * 60 * 1000);
                LogToFile($"[Watchdog] UI in backoff. Waiting {delayMs/1000}s before next attempt.");
                await Task.Delay(delayMs, cancellationToken);
            }

            bool wasStarted = await CheckUIStatusAsync(cancellationToken);
            if (wasStarted)
            {
                _uiFailureCount = 0; // Reset on success
            }
            else
            {
                _uiFailureCount++;
            }
        }

        static async Task<bool> CheckUIStatusAsync(CancellationToken cancellationToken)
        {
            var processes = Process.GetProcessesByName(RansomGuard.Core.Constants.AppIdentifiers.UiProcessName);
            if (processes.Length > 0) return true; // Healthy

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
                        
                        await Task.Delay(2000, cancellationToken);
                        if (Process.GetProcessesByName(RansomGuard.Core.Constants.AppIdentifiers.UiProcessName).Any())
                        {
                            LogToFile("[Watchdog] UI successfully restarted via Alias.");
                            return true;
                        }
                    }
                    catch (Exception aliasEx)
                    {
                        LogToFile($"[Watchdog] Alias launch failed: {aliasEx.Message}");
                    }

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
                            return true;
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
            return false;
        }

        [SupportedOSPlatform("windows")]
        static async Task<bool> CheckServiceStatusAsync(CancellationToken cancellationToken)
        {
            try
            {
#pragma warning disable CA1416
                using ServiceController sc = new(ServiceName);
                try
                {
                    LogToFile($"[Watchdog] Checking service '{ServiceName}' status: {sc.Status}");
                    
                    if (sc.Status == ServiceControllerStatus.Running) return true;

                    if (sc.Status == ServiceControllerStatus.Stopped || sc.Status == ServiceControllerStatus.StopPending)
                    {
                        LogToFile($"[Watchdog] Service is {sc.Status}. Attempting to start...");
                        await Task.Delay(1000, cancellationToken);
                        sc.Refresh();

                        if (sc.Status == ServiceControllerStatus.Stopped)
                        {
                            LogToFile("[Watchdog] Starting service...");
                            sc.Start();
                            sc.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(10));
                            LogToFile("[Watchdog] Service started successfully");
                            return true;
                        }
                    }
                }
                catch (InvalidOperationException ex)
                {
                    LogToFile($"[Watchdog] Service '{ServiceName}' not accessible: {ex.Message}");
                }
                catch (System.ComponentModel.Win32Exception ex)
                {
                    LogToFile($"[Watchdog] Cannot control service (SCM will handle): {ex.Message}");
                }
                catch (System.ServiceProcess.TimeoutException ex)
                {
                    LogToFile($"[Watchdog] Service start timeout: {ex.Message}");
                }
#pragma warning restore CA1416
            }
            catch (Exception ex)
            {
                LogToFile($"[Watchdog] Service check error: {ex.Message}");
            }
            return false;
        }

        static void LogToFile(string message)
        {
            try
            {
                string logDir = Path.Combine(PathConfiguration.GetConfigDirectory(), "Logs");
                if (!Directory.Exists(logDir)) Directory.CreateDirectory(logDir);
                string logPath = Path.Combine(logDir, RansomGuard.Core.Constants.AppIdentifiers.WatchdogLogFile);
                File.AppendAllText(logPath, $"{DateTime.Now}: {message}\n");
            }
            catch { }
            Debug.WriteLine(message);
        }

        static bool IsWatchdogEnabled()
        {
            try
            {
                if (!File.Exists(ConfigPath)) return true;
                string json = File.ReadAllText(ConfigPath);
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("WatchdogEnabled", out var prop))
                    return prop.GetBoolean();
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Watchdog] IsWatchdogEnabled failed: {ex.Message}");
                return true;
            }
        }
    }
}
