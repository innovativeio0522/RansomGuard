using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Microsoft.Win32;

namespace RansomGuard.Service.Engine
{
    public class ActiveResponseService
    {
        private const string QuarantinePath = @"C:\RansomGuard\Quarantine";

        public void KillProcess(int pid)
        {
            try
            {
                var process = Process.GetProcessById(pid);
                string path = process.MainModule?.FileName ?? "";
                
                process.Kill(true);
                Console.WriteLine($"Killed suspicious process: {pid}");

                if (!string.IsNullOrEmpty(path))
                {
                    QuarantineFile(path);
                    ScrubPersistence(path);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to kill process {pid}: {ex.Message}");
            }
        }

        public void QuarantineFile(string filePath)
        {
            try
            {
                if (!Directory.Exists(QuarantinePath))
                {
                    Directory.CreateDirectory(QuarantinePath);
                }

                string fileName = Path.GetFileName(filePath);
                string destPath = Path.Combine(QuarantinePath, fileName + ".quarantine");

                File.Move(filePath, destPath, true);
                Console.WriteLine($"Quarantined file to: {destPath}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Quarantine failed for {filePath}: {ex.Message}");
            }
        }

        public void ScrubPersistence(string maliciousPath)
        {
            try
            {
                // Scrub Registry Run keys
                string[] runKeys = {
                    @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run",
                    @"SOFTWARE\Microsoft\Windows\CurrentVersion\RunOnce"
                };

                foreach (var keyPath in runKeys)
                {
                    using var key = Registry.CurrentUser.OpenSubKey(keyPath, true);
                    if (key != null)
                    {
                        foreach (var valueName in key.GetValueNames())
                        {
                            var value = key.GetValue(valueName)?.ToString();
                            if (value != null && value.Contains(maliciousPath, StringComparison.OrdinalIgnoreCase))
                            {
                                key.DeleteValue(valueName);
                                Console.WriteLine($"Removed persistence from Registry: {valueName}");
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Persistence scrubbing failed: {ex.Message}");
            }
        }

        public void LockdownNetwork()
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "netsh",
                    Arguments = "interface set interface name=\"*\" admin=disabled",
                    CreateNoWindow = true,
                    UseShellExecute = false
                });
                Console.WriteLine("Network Lockdown Engaged.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Network Lockdown failed: {ex.Message}");
            }
        }

        public void PerformEmergencyShutdown()
        {
            try
            {
                Console.WriteLine("!!! CRITICAL THREAT - ENGAGING EMERGENCY SHUTDOWN !!!");
                Process.Start(new ProcessStartInfo
                {
                    FileName = "shutdown.exe",
                    Arguments = "/s /f /t 0",
                    CreateNoWindow = true,
                    UseShellExecute = false
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Emergency Shutdown failed: {ex.Message}");
            }
        }
    }
}
