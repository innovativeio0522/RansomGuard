using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using RansomGuard.Core.Models;

namespace RansomGuard.Core.Interfaces
{
    public interface ISystemMonitorService
    {
        event Action<FileActivity> FileActivityDetected;
        event Action<Threat> ThreatDetected;
        event Action<bool> ConnectionStatusChanged;

        bool IsConnected { get; }

        IEnumerable<Threat> GetRecentThreats();
        IEnumerable<FileActivity> GetRecentFileActivities();
        IEnumerable<ProcessInfo> GetActiveProcesses();
        
        DateTime GetLastScanTime();
        Task PerformQuickScan();

        double GetSystemCpuUsage();
        long GetSystemMemoryUsage();
        int GetMonitoredFilesCount();
        RansomGuard.Core.IPC.TelemetryData GetTelemetry();

        IEnumerable<string> GetQuarantinedFiles();
        double GetQuarantineStorageUsage();
        Task KillProcess(int pid);
    }
}
