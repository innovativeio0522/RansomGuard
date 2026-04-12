using RansomGuard.Models;
using System.Collections.Generic;

namespace RansomGuard.Services
{
    public interface ISystemMonitorService
    {
        IEnumerable<Threat> GetRecentThreats();
        IEnumerable<FileActivity> GetRecentFileActivities();
        IEnumerable<ProcessInfo> GetActiveProcesses();
        
        double GetSystemCpuUsage();
        long GetSystemMemoryUsage();
        int GetMonitoredFilesCount();
    }
}
