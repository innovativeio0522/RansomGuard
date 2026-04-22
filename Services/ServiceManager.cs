using System;
using System.Diagnostics;
using System.IO;
using System.ServiceProcess;

namespace RansomGuard.Services
{
    public class ServiceManager
    {
        private const string ServiceName = "RansomGuardSentinel";
        private const string ServiceDisplayName = "RansomGuard Sentinel Service";
        private const string TaskName = "RansomGuardSilentStart";
        private const string WatchdogProcessName = "RansomGuard.Watchdog";

        public static bool IsServiceInstalled()
        {
            try
            {
                using var controller = new ServiceController(ServiceName);
                var status = controller.Status;
                return true;
            }
            catch
            {
                return false;
            }
        }

        public static void InstallService(string serviceExePath)
        {
            try
            {
                // Install Windows Service
                if (!RunCommand("sc", $"create {ServiceName} binPath= \"{serviceExePath}\" start= auto DisplayName= \"{ServiceDisplayName}\""))
                {
                    throw new Exception("Failed to create service");
                }
                
                if (!RunCommand("sc", $"description {ServiceName} \"Provides proactive ransomware protection and recovery shields.\""))
                {
                    System.Diagnostics.Debug.WriteLine("Failed to set service description (non-critical)");
                }
                
                if (!RunCommand("sc", $"start {ServiceName}"))
                {
                    throw new Exception("Failed to start service");
                }

                StartWatchdog();

                // Register Task Scheduler task... (no change to existing logic thereafter)
                string dashboardPath = Process.GetCurrentProcess().MainModule?.FileName ?? "";
                if (!string.IsNullOrEmpty(dashboardPath))
                {
                    if (!RunCommand("schtasks", $"/create /tn \"{TaskName}\" /tr \"\\\"{dashboardPath}\\\" --startup\" /sc onlogon /rl highest /f"))
                    {
                        System.Diagnostics.Debug.WriteLine("Failed to create scheduled task (non-critical)");
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Installation failed: {ex.Message}");
                throw;
            }
        }

        public static void StartWatchdog()
        {
            try
            {
                var processes = Process.GetProcessesByName(WatchdogProcessName);
                if (processes.Length == 0)
                {
                    string baseDir = AppDomain.CurrentDomain.BaseDirectory;
                    // Look for the watchdog in the same directory as the dashboard
                    string watchdogPath = Path.Combine(baseDir, WatchdogProcessName + ".exe");
                    
                    if (File.Exists(watchdogPath))
                    {
                        Process.Start(new ProcessStartInfo
                        {
                            FileName = watchdogPath,
                            CreateNoWindow = true,
                            UseShellExecute = false, // Keep it stealthy
                            WindowStyle = ProcessWindowStyle.Hidden
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to start watchdog: {ex.Message}");
            }
        }

        public static void StopWatchdog()
        {
            try
            {
                var processes = Process.GetProcessesByName(WatchdogProcessName);
                foreach (var p in processes)
                {
                    p.Kill();
                    p.WaitForExit(2000);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to stop watchdog: {ex.Message}");
            }
        }

        public static void StopService()
        {
            StopWatchdog();
            RunCommand("sc", $"stop {ServiceName}");
        }

        private static bool RunCommand(string fileName, string arguments)
        {
            Process? process = null;
            try
            {
                process = Process.Start(new ProcessStartInfo
                {
                    FileName = fileName,
                    Arguments = arguments,
                    CreateNoWindow = true,
                    UseShellExecute = true,
                    Verb = "runas" // Ensure elevation
                });
                
                if (process == null)
                {
                    System.Diagnostics.Debug.WriteLine($"Failed to start process: {fileName} {arguments}");
                    return false;
                }
                
                process.WaitForExit();
                
                if (process.ExitCode != 0)
                {
                    System.Diagnostics.Debug.WriteLine($"Process exited with code {process.ExitCode}: {fileName} {arguments}");
                    return false;
                }
                
                return true;
            }
            catch (System.ComponentModel.Win32Exception ex)
            {
                // User cancelled UAC prompt or elevation denied
                System.Diagnostics.Debug.WriteLine($"Elevation denied or cancelled: {ex.Message}");
                return false;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"RunCommand error: {ex.Message}");
                return false;
            }
            finally
            {
                process?.Dispose();
            }
        }
    }
}
