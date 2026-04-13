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
                RunCommand("sc", $"create {ServiceName} binPath= \"{serviceExePath}\" start= auto DisplayName= \"{ServiceDisplayName}\"");
                RunCommand("sc", $"description {ServiceName} \"Provides proactive ransomware protection and recovery shields.\"");
                RunCommand("sc", $"start {ServiceName}");

                // Register Task Scheduler task for Silent Admin Startup of the Dashboard
                string dashboardPath = Process.GetCurrentProcess().MainModule?.FileName ?? "";
                if (!string.IsNullOrEmpty(dashboardPath))
                {
                    RunCommand("schtasks", $"/create /tn \"{TaskName}\" /tr \"{dashboardPath}\" /sc onlogon /rl highest /f");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Installation failed: {ex.Message}");
            }
        }

        private static void RunCommand(string fileName, string arguments)
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                CreateNoWindow = true,
                UseShellExecute = true,
                Verb = "runas" // Ensure elevation
            });
            process?.WaitForExit();
        }
    }
}
