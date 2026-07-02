using System;
using System.Diagnostics;
using System.Threading.Tasks;
using RansomGuard.Core.Services;
using RansomGuard.Core.Helpers;
using RansomGuard.Core.Constants;

namespace RansomGuard.Service.Engine
{
    /// <summary>
    /// Handles critical security responses including network isolation,
    /// emergency shutdown, and VSS integrity checks.
    /// </summary>
    public class CriticalResponseService
    {
        /// <summary>
        /// Executes critical response protocols based on configuration.
        /// </summary>
        public void ExecuteCriticalResponse(bool networkIsolationEnabled, bool emergencyShutdownEnabled)
        {
            FileLogger.Log(AppIdentifiers.CriticalResponseLogFile, $"[CRITICAL] ExecuteCriticalResponse triggered. NetworkIsolation={networkIsolationEnabled}, Shutdown={emergencyShutdownEnabled}");

            if (networkIsolationEnabled)
            {
                IsolateNetwork();
            }

            if (emergencyShutdownEnabled)
            {
                EmergencyShutdown();
            }
        }

        /// <summary>
        /// Disables all active network adapters to prevent ransomware spread.
        /// </summary>
        private void IsolateNetwork()
        {
            try
            {
                FileLogger.Log(AppIdentifiers.CriticalResponseLogFile, "[CRITICAL] Running Network Isolation command...");
                
                var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = AppIdentifiers.PowerShellExe,
                        Arguments = "-NoProfile -ExecutionPolicy Bypass -Command \"Get-NetAdapter | Where-Object { $_.Status -eq 'Up' } | Disable-NetAdapter -Confirm:$false\"",
                        UseShellExecute = false,
                        CreateNoWindow = true
                    }
                };
                process.Start();
                
                FileLogger.Log(AppIdentifiers.CriticalResponseLogFile, "[CRITICAL] Network Isolation command started successfully.");
            }
            catch (Exception ex)
            {
                FileLogger.LogError(AppIdentifiers.CriticalResponseLogFile, $"[CRITICAL] Failed to trigger network isolation: {ex.Message}");
            }
        }

        /// <summary>
        /// Triggers an immediate system shutdown to prevent further damage.
        /// </summary>
        private void EmergencyShutdown()
        {
            try
            {
                FileLogger.Log(AppIdentifiers.CriticalResponseLogFile, "[CRITICAL] Triggering Emergency Shutdown...");
                
                Process.Start(new ProcessStartInfo
                {
                    FileName = AppIdentifiers.ShutdownExe,
                    Arguments = "/s /f /t 5 /c \"RansomGuard: Critical Threat Detected. Emergency Shutdown triggered to prevent data loss.\"",
                    CreateNoWindow = true,
                    UseShellExecute = false
                });
                
                FileLogger.Log(AppIdentifiers.CriticalResponseLogFile, "[CRITICAL] Emergency Shutdown command executed.");
            }
            catch (Exception ex)
            {
                FileLogger.LogError(AppIdentifiers.CriticalResponseLogFile, $"[CRITICAL] Failed to trigger shutdown: {ex.Message}");
            }
        }

        /// <summary>
        /// Checks VSS (Volume Shadow Copy Service) integrity to detect if
        /// ransomware has deleted shadow copies.
        /// </summary>
        public async Task CheckVssIntegrityAsync()
        {
            try
            {
                FileLogger.Log(AppIdentifiers.CriticalResponseLogFile, "[VSS] Starting VSS Shield Integrity Check...");
                
                var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = AppIdentifiers.VssAdminExe,
                        Arguments = "list shadows",
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        CreateNoWindow = true
                    }
                };
                
                process.Start();
                string output = await process.StandardOutput.ReadToEndAsync();
                await process.WaitForExitAsync();

                if (string.IsNullOrEmpty(output) || output.Contains("No items found"))
                {
                    FileLogger.LogError(AppIdentifiers.CriticalResponseLogFile, "[VSS] WARNING: No Shadow Copies found! Ransomware may have deleted them.");
                }
                else
                {
                    FileLogger.Log(AppIdentifiers.CriticalResponseLogFile, "[VSS] Shadow Copies verified as intact.");
                }
            }
            catch (Exception ex)
            {
                FileLogger.LogError(AppIdentifiers.CriticalResponseLogFile, $"[VSS] Check failed: {ex.Message}");
            }
        }
    }
}
