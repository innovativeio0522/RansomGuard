using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Microsoft.Win32;
using RansomGuard.Core.Models;
using RansomGuard.Core.Helpers;
using RansomGuard.Core.Constants;
using RansomGuard.Core.Configuration;

namespace RansomGuard.Service.Engine
{
    public class ActiveResponseService
    {
        public void KillProcess(int pid)
        {
            if (pid <= 0 || pid > 65535)
            {
                FileLogger.LogWarning(AppIdentifiers.ActiveResponseLogFile, $"KillProcess blocked: Invalid PID {pid}");
                return;
            }

            try
            {
                RetryHelper.Execute(() =>
                {
                    // Check if process still exists
                    Process process;
                    try 
                    { 
                        process = Process.GetProcessById(pid); 
                    }
                    catch (ArgumentException)
                    {
                        FileLogger.LogWarning(AppIdentifiers.ActiveResponseLogFile, $"KillProcess skipped: Process {pid} no longer exists.");
                        return;
                    }

                    string path = string.Empty;
                    try { path = process.MainModule?.FileName ?? ""; } catch { }
                    
                    process.Kill(true);
                    FileLogger.Log(AppIdentifiers.ActiveResponseLogFile, $"Killed suspicious process: {pid}");

                    if (!string.IsNullOrEmpty(path))
                    {
                        QuarantineFile(path);
                        ScrubPersistence(path);
                    }
                }, maxRetries: 2, shouldRetry: ex => ex is InvalidOperationException || ex is System.ComponentModel.Win32Exception);
            }
            catch (Exception ex)
            {
                FileLogger.LogError(AppIdentifiers.ActiveResponseLogFile, $"Failed to kill process {pid}", ex);
            }
        }

        public void QuarantineFile(string filePath)
        {
            try
            {
                string quarantinePath = PathConfiguration.QuarantinePath;
                Directory.CreateDirectory(quarantinePath);

                string fileName = Path.GetFileName(filePath);
                string destPath = Path.Combine(quarantinePath, fileName + ".quarantine");

                RetryHelper.Execute(() => 
                {
                    File.Move(filePath, destPath, true);
                }, maxRetries: 5, initialDelayMs: 200, shouldRetry: ex => ex is IOException);
                
                FileLogger.Log(AppIdentifiers.ActiveResponseLogFile, $"Quarantined file to: {destPath}");
            }
            catch (Exception ex)
            {
                FileLogger.LogError(AppIdentifiers.ActiveResponseLogFile, $"Quarantine failed for {filePath}", ex);
            }
        }

        public void ScrubPersistence(string maliciousPath)
        {
            if (string.IsNullOrWhiteSpace(maliciousPath)) return;

            // SECURITY: Sanitize path to prevent registry value spoofing or injection
            if (maliciousPath.Intersect(Path.GetInvalidPathChars()).Any() || maliciousPath.Contains(';'))
            {
                FileLogger.LogWarning(AppIdentifiers.ActiveResponseLogFile, $"ScrubPersistence blocked: Suspicious path characters in {maliciousPath}");
                return;
            }

            try
            {
                // Scrub Registry Run keys
                string[] runKeys = {
                    AppIdentifiers.RegistryRunKey,
                    AppIdentifiers.RegistryRunOnceKey
                };

                foreach (var keyPath in runKeys)
                {
                    try
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
                                    FileLogger.Log(AppIdentifiers.ActiveResponseLogFile, $"Removed persistence from Registry: {valueName} ({keyPath})");
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        FileLogger.LogWarning(AppIdentifiers.ActiveResponseLogFile, $"Failed to scrub Registry key {keyPath}: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                FileLogger.LogError(AppIdentifiers.ActiveResponseLogFile, "Persistence scrubbing failed", ex);
            }
        }

        public void LockdownNetwork()
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = AppIdentifiers.PowerShellExe,
                    Arguments = "-NoProfile -ExecutionPolicy Bypass -Command \"Get-NetAdapter | Where-Object { $_.Status -eq 'Up' } | Disable-NetAdapter -Confirm:$false\"",
                    CreateNoWindow = true,
                    UseShellExecute = false
                });
                FileLogger.Log(AppIdentifiers.ActiveResponseLogFile, "Network Lockdown Engaged.");
            }
            catch (Exception ex)
            {
                FileLogger.LogError(AppIdentifiers.ActiveResponseLogFile, "Network Lockdown failed", ex);
            }
        }

        public void PerformEmergencyShutdown()
        {
            try
            {
                FileLogger.Log(AppIdentifiers.ActiveResponseLogFile, "!!! CRITICAL THREAT - ENGAGING EMERGENCY SHUTDOWN !!!");
                Process.Start(new ProcessStartInfo
                {
                    FileName = AppIdentifiers.ShutdownExe,
                    Arguments = "/s /f /t 0",
                    CreateNoWindow = true,
                    UseShellExecute = false
                });
            }
            catch (Exception ex)
            {
                FileLogger.LogError(AppIdentifiers.ActiveResponseLogFile, "Emergency shutdown failed", ex);
            }
        }
    }
}
