using RansomGuard.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace RansomGuard.Services
{
    public class MockMonitorService : ISystemMonitorService
    {
        private readonly Random _random = new Random();

        public IEnumerable<Threat> GetRecentThreats()
        {
            return new List<Threat>
            {
                new Threat { Name = "Suspicious modification", Path = @"C:\System32\drivers\etc\hosts", ProcessName = "sys_updater.exe", Severity = ThreatSeverity.Medium },
                new Threat { Name = "Unauthorized Port Access", Path = "Network: 192.168.1.14", ProcessName = "unknown_service.exe", Severity = ThreatSeverity.Low },
                new Threat { Name = "Driver Signature Warning", Path = "vbus_adapter.sys", ProcessName = "kernel", Severity = ThreatSeverity.Low }
            };
        }

        public IEnumerable<FileActivity> GetRecentFileActivities()
        {
            return new List<FileActivity>
            {
                new FileActivity { Action = "WRITE", FilePath = @"C:\System32\drivers\etc\hosts", ProcessName = "sys_updater.exe", IsSuspicious = true },
                new FileActivity { Action = "READ", FilePath = @"D:\User\Documents\Report_Q3.pdf", ProcessName = "Explorer.exe" },
                new FileActivity { Action = "DELETE", FilePath = @"C:\Users\Admin\Desktop\temp_log.txt", ProcessName = "PowerShell.exe" },
                new FileActivity { Action = "BATCH READ", FilePath = @"C:\Users\Admin\AppData\Local\Cache", ProcessName = "Chrome.exe", IsSuspicious = true },
                new FileActivity { Action = "EXECUTE", FilePath = @"D:\Project\source\main.exe", ProcessName = "Visual Studio" }
            };
        }

        public IEnumerable<ProcessInfo> GetActiveProcesses()
        {
            return Enumerable.Range(1, 10).Select(i => new ProcessInfo
            {
                Pid = 1000 + i,
                Name = $"Process_{i}.exe",
                CpuUsage = _random.NextDouble() * 10,
                MemoryUsage = _random.Next(100, 500) * 1024 * 1024
            });
        }

        public double GetSystemCpuUsage() => 12.0 + (_random.NextDouble() * 5);
        public long GetSystemMemoryUsage() => 4500000000L + _random.Next(-100000000, 100000000);
        public int GetMonitoredFilesCount() => 1245678;
    }
}
