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

                // Register Task Scheduler task for silent admin startup of the Dashboard.
                // --startup tells the app to skip the splash screen and go straight to the tray.
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
