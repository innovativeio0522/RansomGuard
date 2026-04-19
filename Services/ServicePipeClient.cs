using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using RansomGuard.Core.Interfaces;
using RansomGuard.Core.Models;
using RansomGuard.Core.IPC;
using RansomGuard.Core.Services;
using RansomGuard.Core.Helpers;

namespace RansomGuard.Services
{
    public class ServicePipeClient : ISystemMonitorService, IDisposable
    {
        private const string PipeName = "SentinelGuardPipe";
        private const int MaxRecentActivities = 200;
        private const int MaxActiveProcesses = 50;
        private const int Windows11BuildNumber = 20348;
        
        private NamedPipeClientStream? _pipeClient;
        private StreamWriter? _writer;
        private CancellationTokenSource? _cts;
        private readonly Random _rng = new();
        private bool _disposed;

        public event Action<FileActivity>? FileActivityDetected;
        public event Action<Threat>? ThreatDetected;
        public event Action<bool>? ConnectionStatusChanged;
        public event Action<ScanSummary>? ScanCompleted;
        public event Action? ProcessListUpdated;
        public event Action<TelemetryData>? TelemetryUpdated;

        public bool IsConnected { get; private set; }

        private readonly List<FileActivity> _recentActivities = new();
        private readonly List<Threat> _recentThreats = new();
        private readonly object _activitiesLock = new();
        private readonly object _threatsLock = new();

        private TelemetryData _lastTelemetry = new();
        private List<ProcessInfo> _lastProcesses = new();
        private readonly object _telemetryLock = new();
        private readonly object _processLock = new object();
        private readonly HashSet<string> _processedEventIds = new();

        public ServicePipeClient()
        {
            Start();
        }



        public void Start()
        {
            _cts = new CancellationTokenSource();
            Task.Run(() => ConnectLoop(_cts.Token));
        }

        private async Task ConnectLoop(CancellationToken token)
        {
            int retryDelayMs = 2000; // Starts at 2s, doubles up to 30s (exponential backoff)
            const int MaxRetryDelayMs = 30_000;

            while (!token.IsCancellationRequested)
            {
                NamedPipeClientStream? pipeClient = null;
                StreamWriter? writer = null;
                StreamReader? reader = null;
                
                try
                {
                    pipeClient = new NamedPipeClientStream(".", PipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
                    await pipeClient.ConnectAsync(5000, token);
                    
                    // Successful connection — reset backoff
                    retryDelayMs = 2000;
                    IsConnected = true;
                    
                    // Clear previous session data to prevent buildup/duplication on reconnect
                    lock (_activitiesLock) { _recentActivities.Clear(); }
                    lock (_threatsLock) { _recentThreats.Clear(); }

                    ConnectionStatusChanged?.Invoke(true);

                    writer = new StreamWriter(pipeClient) { AutoFlush = true };
                    reader = new StreamReader(pipeClient);
                    
                    _pipeClient = pipeClient;
                    _writer = writer;

                    while (pipeClient.IsConnected && !token.IsCancellationRequested)
                    {
                        var line = await reader.ReadLineAsync(token);
                        if (line == null) break;

                        HandlePacket(line);
                    }
                }
                catch (OperationCanceledException) { break; }
                catch (Exception ex)
                {
                    if (IsConnected)
                    {
                        IsConnected = false;
                        ConnectionStatusChanged?.Invoke(false);
                    }
                    
                    // Add jitter (±10%) to avoid thundering-herd if multiple clients reconnect simultaneously
                    int jitter = (int)(retryDelayMs * 0.1 * (_rng.NextDouble() * 2 - 1));
                    int delayWithJitter = Math.Clamp(retryDelayMs + jitter, 1000, MaxRetryDelayMs);
                    System.Diagnostics.Debug.WriteLine(
                        $"ConnectLoop error: {ex.Message}. Retrying in {delayWithJitter / 1000.0:F1}s...");
                    
                    // Dispose resources on error
                    reader?.Dispose();
                    writer?.Dispose();
                    pipeClient?.Dispose();
                    
                    if (!token.IsCancellationRequested)
                        await Task.Delay(delayWithJitter, token);

                    // Double the delay for next retry, capped at max
                    retryDelayMs = Math.Min(retryDelayMs * 2, MaxRetryDelayMs);
                }
                finally
                {
                    if (IsConnected)
                    {
                        IsConnected = false;
                        ConnectionStatusChanged?.Invoke(false);
                    }
                    
                    // Clean up resources
                    reader?.Dispose();
                    writer?.Dispose();
                    pipeClient?.Dispose();
                    
                    _writer = null;
                    _pipeClient = null;
                }
            }
        }

        private void HandlePacket(string json)
        {
            try
            {
                var packet = JsonSerializer.Deserialize<IpcPacket>(json);
                if (packet == null) return;

                // Validate version
                if (packet.Version != IpcPacket.CurrentVersion)
                {
                    System.Diagnostics.Debug.WriteLine($"IPC version mismatch: received {packet.Version}, expected {IpcPacket.CurrentVersion}");
                    return;
                }

                switch (packet.Type)
                {
                    case MessageType.FileActivity:
                    case MessageType.FileActivitySnapshot:
                        var activity = JsonSerializer.Deserialize<FileActivity>(packet.Payload);
                        if (activity != null)
                        {
                            lock (_activitiesLock)
                            {
                                // Global Deduplication: Ignore if we've already processed this exact event Id
                                if (_processedEventIds.Contains(activity.Id)) return;
                                _processedEventIds.Add(activity.Id);
                                
                                // Limit set size to prevent memory leak
                                if (_processedEventIds.Count > 1000) _processedEventIds.Remove(_processedEventIds.First());

                                _recentActivities.Insert(0, activity);
                                if (_recentActivities.Count > MaxRecentActivities) _recentActivities.RemoveAt(_recentActivities.Count - 1);
                            }
                            
                            // Only fire UI event for LIVE activities, not for snapshots
                            if (packet.Type == MessageType.FileActivity)
                            {
                                System.Diagnostics.Debug.WriteLine($"[IPC] Received LIVE FileActivity: {activity.FilePath} ({activity.Action})");
                                FileActivityDetected?.Invoke(activity);
                            }
                        }
                        break;

                     case MessageType.ThreatDetected:
                     case MessageType.ThreatDetectedSnapshot:
                         var threat = JsonSerializer.Deserialize<Threat>(packet.Payload);
                         if (threat != null)
                         {
                             lock (_threatsLock)
                             {
                                 // Deduplicate by Path: update if exists, otherwise insert
                                 var existing = _recentThreats.FirstOrDefault(t => string.Equals(t.Path, threat.Path, StringComparison.OrdinalIgnoreCase));
                                 if (existing != null)
                                 {
                                     // Only update if not already handled by user
                                     if (existing.ActionTaken != "Quarantined" && existing.ActionTaken != "Ignored")
                                     {
                                         existing.ActionTaken = threat.ActionTaken;
                                     }
                                     existing.Severity = threat.Severity;
                                     existing.Timestamp = threat.Timestamp;
                                     existing.Description = threat.Description;
                                 }
                                 else
                                 {
                                     _recentThreats.Insert(0, threat);
                                 }
                             }
                             
                             // Only fire UI event for LIVE threats, not for snapshots
                             if (packet.Type == MessageType.ThreatDetected)
                             {
                                 ThreatDetected?.Invoke(threat);
                             }
                         }
                         break;

                    case MessageType.TelemetryUpdate:
                        var tele = JsonSerializer.Deserialize<TelemetryData>(packet.Payload);
                        if (tele != null)
                        {
                            lock (_telemetryLock) { _lastTelemetry = tele; }
                            TelemetryUpdated?.Invoke(tele);
                        }
                        break;
                    case MessageType.ScanCompleted:
                        var summary = JsonSerializer.Deserialize<ScanSummary>(packet.Payload);
                        if (summary != null)
                        {
                            ScanCompleted?.Invoke(summary);
                        }
                        break;
                    case MessageType.ProcessListUpdate:
                        var procs = JsonSerializer.Deserialize<List<ProcessInfo>>(packet.Payload);
                        if (procs != null)
                        {
                            lock (_processLock) { _lastProcesses = procs; }
                            System.Diagnostics.Debug.WriteLine($"[IPC] Received {procs.Count} processes from service");
                            ProcessListUpdated?.Invoke();
                        }
                        break;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"HandlePacket error: {ex.Message}");
            }
        }

        private async Task SendCommand(CommandType command, string args = "")
        {
            if (_writer == null || _pipeClient?.IsConnected != true) return;

            var request = new CommandRequest { Command = command, Arguments = args };
            var packet = new IpcPacket
            {
                Type = MessageType.CommandRequest,
                Payload = JsonSerializer.Serialize(request)
            };
            await _writer.WriteLineAsync(JsonSerializer.Serialize(packet));
        }

        public IEnumerable<Threat> GetRecentThreats()
        {
            lock (_threatsLock) { return _recentThreats.ToList(); }
        }

        public IEnumerable<FileActivity> GetRecentFileActivities()
        {
            lock (_activitiesLock) { return _recentActivities.ToList(); }
        }

        public IEnumerable<ProcessInfo> GetActiveProcesses()
        {
            if (IsConnected)
            {
                lock (_processLock)
                {
                    return _lastProcesses.ToList();
                }
            }

            return Enumerable.Empty<ProcessInfo>();
        }
        
        public DateTime GetLastScanTime()
        {
            if (IsConnected)
            {
                lock (_telemetryLock) { return _lastTelemetry.LastScanTime; }
            }
            return _lastScanTime;
        }
        private DateTime _lastScanTime = ConfigurationService.Instance.LastScanTime;

        public async Task PerformQuickScan()
        {
            if (IsConnected)
            {
                await SendCommand(CommandType.PerformScan);
                _lastScanTime = DateTime.Now;
                ConfigurationService.Instance.LastScanTime = _lastScanTime;
                ConfigurationService.Instance.TotalScansCount++;
                ConfigurationService.Instance.Save();
            }
        }

        public double GetSystemCpuUsage() { lock (_telemetryLock) { return _lastTelemetry.CpuUsage; } }
        public long GetSystemMemoryUsage() { lock (_telemetryLock) { return _lastTelemetry.MemoryUsage; } }
        // Read from the config (shared source of truth) rather than the service's active watcher count.
        // The service runs as LocalSystem and may not see all user drives (e.g. D:\), causing its
        // _watchers.Count to be lower than the configured paths count — the Settings page bug.
        public int GetMonitoredFilesCount() { return ConfigurationService.Instance.MonitoredPaths.Count; }
        public TelemetryData GetTelemetry() { lock (_telemetryLock) { return _lastTelemetry; } }
        public double GetQuarantineStorageUsage() 
        { 
            if (IsConnected)
            {
                lock (_telemetryLock) { return _lastTelemetry.QuarantineStorageMb; }
            }

            // Local implementation fallback
            try
            {
                if (!Directory.Exists(PathConfiguration.QuarantinePath)) return 0;
                var files = Directory.GetFiles(PathConfiguration.QuarantinePath, "*.quarantine");
                long totalBytes = files.Sum(f => new FileInfo(f).Length);
                return totalBytes / (1024.0 * 1024.0);
            }
            catch { return 0; }
        }

        public IEnumerable<string> GetQuarantinedFiles()
        {
            try
            {
                if (!Directory.Exists(PathConfiguration.QuarantinePath)) return Enumerable.Empty<string>();
                return Directory.GetFiles(PathConfiguration.QuarantinePath, "*.quarantine");
            }
            catch { return Enumerable.Empty<string>(); }
        }
        public async Task KillProcess(int pid) => await SendCommand(CommandType.KillProcess, pid.ToString());

        /// <summary>
        /// No-op on the client side — watcher re-initialisation is triggered via the UpdatePaths IPC command to the service.
        /// </summary>
        public void InitializeWatchers() => _ = SendCommand(CommandType.UpdatePaths);

        public async Task QuarantineFile(string filePath)
        {
            if (IsConnected)
            {
                await SendCommand(CommandType.QuarantineFile, filePath);
            }
            lock (_threatsLock)
            {
                var matchingThreats = _recentThreats.Where(t => string.Equals(t.Path, filePath, StringComparison.OrdinalIgnoreCase));
                foreach (var t in matchingThreats) t.ActionTaken = "Quarantined";
            }
        }

        public async Task RestoreQuarantinedFile(string quarantinePath)
        {
            if (IsConnected)
            {
                await SendCommand(CommandType.RestoreFile, quarantinePath);
            }
        }

        public async Task DeleteQuarantinedFile(string quarantinePath)
        {
            if (IsConnected)
            {
                await SendCommand(CommandType.DeleteFile, quarantinePath);
            }
        }

        public async Task ClearSafeFiles()
        {
            if (IsConnected)
            {
                await SendCommand(CommandType.ClearSafeFiles, string.Empty);
            }
        }

        public async Task WhitelistProcess(string name) => await SendCommand(CommandType.WhitelistProcess, name);
        public async Task RemoveWhitelist(string name) => await SendCommand(CommandType.RemoveWhitelist, name);

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            // Cancel connection loop
            _cts?.Cancel();
            _cts?.Dispose();

            // Dispose pipe resources
            _writer?.Dispose();
            _pipeClient?.Dispose();
        }
    }
}
