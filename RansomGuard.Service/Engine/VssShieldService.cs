using System;
using System.Management;
using System.Diagnostics;
using RansomGuard.Core.Models;

namespace RansomGuard.Service.Engine
{
    public class VssShieldService
    {
        private readonly SentinelEngine _engine;
        private ManagementEventWatcher? _processWatcher;

        public VssShieldService(SentinelEngine engine)
        {
            _engine = engine;
        }

        public void Start()
        {
            if (!OperatingSystem.IsWindows()) return;

            try
            {
                // Monitor for process creation events
                _processWatcher = new ManagementEventWatcher(new WqlEventQuery("SELECT * FROM Win32_ProcessStartTrace"));
                _processWatcher.EventArrived += (s, e) => 
                {
                    string processName = e.NewEvent.Properties["ProcessName"].Value.ToString() ?? "";
                    uint processId = (uint)e.NewEvent.Properties["ProcessID"].Value;

                    CheckProcess(processName, (int)processId);
                };
                _processWatcher.Start();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"VSS Shield Error: {ex.Message}");
            }
        }

        private void CheckProcess(string name, int pid)
        {
            // Use culture-invariant comparison for better performance and correctness
            if (name.Equals("vssadmin.exe", StringComparison.OrdinalIgnoreCase) || 
                name.Equals("powershell.exe", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    using var process = Process.GetProcessById(pid);
                    // Use a more robust way to get command line in production (e.g. WMI query for the specific PID)
                    // For now, we'll flag any attempt to run vssadmin from a non-system process
                    
                    if (name.Equals("vssadmin.exe", StringComparison.OrdinalIgnoreCase))
                    {
                        _engine.ReportThreat("VSS_SUBSYSTEM", $"Suspicious VSS interaction by {name}", "A process attempted to interact with the Volume Shadow Copy service in a way that often precedes ransomware encryption.", ThreatSeverity.High);
                        // Force kill if it's likely a deletion attempt
                        process.Kill();
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"CheckProcess error: {ex.Message}");
                }
            }
        }

        public void Stop()
        {
            _processWatcher?.Stop();
            _processWatcher?.Dispose();
        }
    }
}
