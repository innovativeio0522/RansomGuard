using System;
using System.Management;
using System.Diagnostics;
using RansomGuard.Core.Models;
using RansomGuard.Core.Constants;
using RansomGuard.Core.Configuration;

namespace RansomGuard.Service.Engine
{
    public class VssShieldService
    {
        private readonly SentinelEngine _engine;
        private readonly IEtwMonitorService? _etwMonitor;

        public VssShieldService(SentinelEngine engine, IEtwMonitorService? etwMonitor = null)
        {
            _engine = engine;
            _etwMonitor = etwMonitor;
        }

        public void Start()
        {
            if (!OperatingSystem.IsWindows()) return;

            if (_etwMonitor != null)
            {
                _etwMonitor.ProcessStarted += (e) => CheckProcess(e.ProcessName, e.ProcessId, e.CommandLine);
            }
        }

        private void CheckProcess(string name, int pid, string? etwCommandLine = null)
        {
            // Use culture-invariant comparison for better performance and correctness
            if (name.Equals(AppIdentifiers.VssAdminExe, StringComparison.OrdinalIgnoreCase) || 
                name.Equals(AppIdentifiers.PowerShellExe, StringComparison.OrdinalIgnoreCase) ||
                name.Equals(AppIdentifiers.WmicExe, StringComparison.OrdinalIgnoreCase) ||
                name.Equals(AppIdentifiers.WbAdminExe, StringComparison.OrdinalIgnoreCase) ||
                name.Equals(AppIdentifiers.BcdEditExe, StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    string commandLine = etwCommandLine ?? GetCommandLine(pid);
                    bool isMalicious = false;
                    string threatReason = "";

                    if (name.Equals(AppIdentifiers.VssAdminExe, StringComparison.OrdinalIgnoreCase))
                    {
                        if (commandLine.Contains(AppIdentifiers.CmdDelete, StringComparison.OrdinalIgnoreCase) && 
                            commandLine.Contains(AppIdentifiers.CmdShadows, StringComparison.OrdinalIgnoreCase))
                        {
                            isMalicious = true;
                            threatReason = "Shadow copy deletion attempt via vssadmin";
                        }
                    }
                    else if (name.Equals(AppIdentifiers.PowerShellExe, StringComparison.OrdinalIgnoreCase))
                    {
                        if (commandLine.Contains(AppIdentifiers.CmdWin32ShadowCopy, StringComparison.OrdinalIgnoreCase) && 
                           (commandLine.Contains(AppIdentifiers.CmdDelete, StringComparison.OrdinalIgnoreCase) || 
                            commandLine.Contains(AppIdentifiers.CmdRemoveWmiObject, StringComparison.OrdinalIgnoreCase) ||
                            commandLine.Contains(AppIdentifiers.CmdRemoveCimInstance, StringComparison.OrdinalIgnoreCase)))
                        {
                            isMalicious = true;
                            threatReason = "Shadow copy deletion attempt via PowerShell";
                        }
                    }
                    else if (name.Equals(AppIdentifiers.WmicExe, StringComparison.OrdinalIgnoreCase))
                    {
                        if (commandLine.Contains(AppIdentifiers.CmdShadows, StringComparison.OrdinalIgnoreCase) && 
                            commandLine.Contains(AppIdentifiers.CmdDelete, StringComparison.OrdinalIgnoreCase))
                        {
                            isMalicious = true;
                            threatReason = "Shadow copy deletion attempt via WMIC";
                        }
                    }
                    else if (name.Equals(AppIdentifiers.WbAdminExe, StringComparison.OrdinalIgnoreCase))
                    {
                        if (commandLine.Contains(AppIdentifiers.CmdDelete, StringComparison.OrdinalIgnoreCase) && 
                            (commandLine.Contains(AppIdentifiers.CmdCatalog, StringComparison.OrdinalIgnoreCase) || 
                             commandLine.Contains(AppIdentifiers.CmdSystemStateBackup, StringComparison.OrdinalIgnoreCase)))
                        {
                            isMalicious = true;
                            threatReason = "Backup catalog deletion attempt via wbadmin";
                        }
                    }
                    else if (name.Equals(AppIdentifiers.BcdEditExe, StringComparison.OrdinalIgnoreCase))
                    {
                        if (commandLine.Contains(AppIdentifiers.CmdSet, StringComparison.OrdinalIgnoreCase) && 
                            commandLine.Contains(AppIdentifiers.CmdRecoveryEnabled, StringComparison.OrdinalIgnoreCase) && 
                            commandLine.Contains(AppIdentifiers.CmdNo, StringComparison.OrdinalIgnoreCase))
                        {
                            isMalicious = true;
                            threatReason = "Disabling recovery mode via bcdedit";
                        }
                    }

                    if (isMalicious)
                    {
                        using var process = Process.GetProcessById(pid);
                        _engine.ReportThreat("VSS_SUBSYSTEM", threatReason, 
                            $"A suspicious command was detected: {commandLine}", 
                            name, pid, ThreatSeverity.High);
                        
                        process.Kill(true);
                        RansomGuard.Core.Helpers.FileLogger.Log(AppIdentifiers.VssShieldLogFile, $"TERMINATED malicious process {name} (PID: {pid}). Command: {commandLine}");
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"CheckProcess error: {ex.Message}");
                }
            }
        }

        private string GetCommandLine(int pid)
        {
            try
            {
                using var searcher = new ManagementObjectSearcher($"SELECT CommandLine FROM Win32_Process WHERE ProcessId = {pid}");
                using var objects = searcher.Get();
                foreach (ManagementObject obj in objects)
                {
                    return obj["CommandLine"]?.ToString() ?? "";
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"GetCommandLine error: {ex.Message}");
            }
            return "";
        }

        public void Stop()
        {
            // ETW session is managed by the Worker/EtwMonitorService directly
        }
    }
}
