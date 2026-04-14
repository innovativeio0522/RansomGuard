using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using RansomGuard.Core.Models;
using RansomGuard.Core.IPC;

namespace RansomGuard.Core.Interfaces
{
    /// <summary>
    /// Provides system monitoring and threat detection services for ransomware protection.
    /// </summary>
    public interface ISystemMonitorService
    {
        /// <summary>
        /// Raised when a file activity is detected in monitored directories.
        /// </summary>
        event Action<FileActivity> FileActivityDetected;
        
        /// <summary>
        /// Raised when a potential threat is detected by the monitoring engine.
        /// </summary>
        event Action<Threat> ThreatDetected;
        
        /// <summary>
        /// Raised when the connection status to the background service changes.
        /// </summary>
        event Action<bool> ConnectionStatusChanged;

        /// <summary>
        /// Raised when a system-wide scan completes with a summary of findings.
        /// </summary>
        event Action<ScanSummary> ScanCompleted;
        
        /// <summary>
        /// Raised when the process list has been updated with fresh data from the service.
        /// </summary>
        event Action ProcessListUpdated;

        /// <summary>
        /// Gets a value indicating whether the service is connected to the background monitoring engine.
        /// </summary>
        bool IsConnected { get; }

        /// <summary>
        /// Retrieves the most recent threats detected by the system.
        /// </summary>
        /// <returns>A collection of recent threat objects.</returns>
        IEnumerable<Threat> GetRecentThreats();
        
        /// <summary>
        /// Retrieves the most recent file activities detected in monitored directories.
        /// </summary>
        /// <returns>A collection of recent file activity objects.</returns>
        IEnumerable<FileActivity> GetRecentFileActivities();
        
        /// <summary>
        /// Retrieves information about currently active processes on the system.
        /// </summary>
        /// <returns>A collection of process information objects.</returns>
        IEnumerable<ProcessInfo> GetActiveProcesses();
        
        /// <summary>
        /// Gets the timestamp of the last completed security scan.
        /// </summary>
        /// <returns>The date and time of the last scan, or DateTime.MinValue if never scanned.</returns>
        DateTime GetLastScanTime();
        
        /// <summary>
        /// Performs a quick scan of monitored directories for suspicious files and activities.
        /// </summary>
        /// <returns>A task representing the asynchronous scan operation.</returns>
        Task PerformQuickScan();

        /// <summary>
        /// Gets the current system-wide CPU usage percentage.
        /// </summary>
        /// <returns>CPU usage as a percentage (0-100).</returns>
        double GetSystemCpuUsage();
        
        /// <summary>
        /// Gets the current memory usage of the monitoring service in bytes.
        /// </summary>
        /// <returns>Memory usage in bytes.</returns>
        long GetSystemMemoryUsage();
        
        /// <summary>
        /// Gets the count of files currently being monitored.
        /// </summary>
        /// <returns>The number of monitored files.</returns>
        int GetMonitoredFilesCount();
        
        /// <summary>
        /// Retrieves comprehensive telemetry data from the monitoring service.
        /// </summary>
        /// <returns>A telemetry data object containing system metrics and status information.</returns>
        RansomGuard.Core.IPC.TelemetryData GetTelemetry();

        /// <summary>
        /// Retrieves a list of files currently in quarantine.
        /// </summary>
        /// <returns>A collection of file paths for quarantined files.</returns>
        IEnumerable<string> GetQuarantinedFiles();
        
        /// <summary>
        /// Gets the total storage space used by quarantined files in megabytes.
        /// </summary>
        /// <returns>Storage usage in megabytes.</returns>
        double GetQuarantineStorageUsage();
        
        /// <summary>
        /// Terminates a process by its process ID.
        /// </summary>
        /// <param name="pid">The process ID to terminate.</param>
        /// <returns>A task representing the asynchronous operation.</returns>
        Task KillProcess(int pid);
        
        /// <summary>
        /// Moves a suspicious file to quarantine to prevent further harm.
        /// </summary>
        /// <param name="filePath">The full path to the file to quarantine.</param>
        /// <returns>A task representing the asynchronous operation.</returns>
        Task QuarantineFile(string filePath);

        /// <summary>
        /// Re-initialises all FileSystemWatcher instances based on the current monitored paths configuration.
        /// Should be called after monitored paths are added or removed.
        /// </summary>
        void InitializeWatchers();

        /// <summary>
        /// Restores a file from quarantine to its original location.
        /// </summary>
        /// <param name="quarantinePath">The path to the file in the quarantine silo.</param>
        /// <returns>A task representing the asynchronous operation.</returns>
        Task RestoreQuarantinedFile(string quarantinePath);

        /// <summary>
        /// Permanently deletes a file from the quarantine silo.
        /// </summary>
        /// <param name="quarantinePath">The path to the file in the quarantine silo.</param>
        /// <returns>A task representing the asynchronous operation.</returns>
        Task DeleteQuarantinedFile(string quarantinePath);

        /// <summary>
        /// Clears files from quarantine that are deemed safe.
        /// </summary>
        /// <returns>A task representing the asynchronous operation.</returns>
        Task ClearSafeFiles();

        /// <summary>
        /// Adds a process name to the persistent whitelist.
        /// </summary>
        Task WhitelistProcess(string name);

        /// <summary>
        /// Removes a process name from the persistent whitelist.
        /// </summary>
        Task RemoveWhitelist(string name);
    }
}
