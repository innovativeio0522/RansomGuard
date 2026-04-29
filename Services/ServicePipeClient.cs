using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using RansomGuard.Core.Interfaces;
using RansomGuard.Core.Models;
using RansomGuard.Core.IPC;
using RansomGuard.Core.Services;
using RansomGuard.Core.Helpers;
using RansomGuard.Core.Configuration;

namespace RansomGuard.Services
{
    public class ServicePipeClient : ISystemMonitorService, IDisposable
    {
        private readonly string _pipeName;
        private const int MaxRecentActivities = AppConstants.Limits.MaxRecentActivities;
        private const int MaxRecentThreats = AppConstants.Limits.MaxRecentThreats;
        
        private NamedPipeClientStream? _pipeClient;
        private StreamWriter? _writer;
        private CancellationTokenSource? _cts;
        private readonly SemaphoreSlim _writeSemaphore = new(1, 1);
        private readonly Random _rng = new();
        private bool _disposed;
        private long _nextSequenceId = 1;

        public event Action<FileActivity>? FileActivityDetected;
        public event Action<Threat>? ThreatDetected;
        public event Action<bool>? ConnectionStatusChanged;

        public event Action? ProcessListUpdated;
        public event Action<TelemetryData>? TelemetryUpdated;
        public event Action<LanPeerListUpdate>? LanPeerListUpdated;

        public bool IsConnected { get; private set; }
        public bool IsHandshaked { get; private set; }

        private readonly List<FileActivity> _recentActivities = new();
        private readonly List<Threat> _recentThreats = new();
        private readonly object _activitiesLock = new();
        private readonly object _threatsLock = new();

        private TelemetryData _lastTelemetry = new();
        private List<ProcessInfo> _lastProcesses = new();
        private readonly object _telemetryLock = new();
        private readonly object _processLock = new object();
        private readonly HashSet<string> _processedEventIds = new();
        private readonly object _eventIdsLock = new();

        public ServicePipeClient(string pipeName = "SentinelGuardPipeV2")
        {
            _pipeName = pipeName;
            Start();
        }

        public void Start()
        {
            _cts = new CancellationTokenSource();
            Task.Run(() => ConnectLoop(_cts.Token));
        }

        private async Task ConnectLoop(CancellationToken token)
        {
            int retryDelayMs = AppConstants.Ipc.InitialRetryDelayMs;
            const int MaxRetryDelayMs = AppConstants.Ipc.MaxRetryDelayMs;

            while (!token.IsCancellationRequested)
            {
                try
                {
                    using var pipeClient = new NamedPipeClientStream(".", _pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
                    FileLogger.Log("ipc_client.log", $"[IPC Client] Attempting to connect to: {_pipeName}");
                    await pipeClient.ConnectAsync(AppConstants.Ipc.ConnectionTimeoutMs, token);
                    FileLogger.Log("ipc_client.log", "[IPC Client] Connected to pipe server!");
                    
                    using var writer = new StreamWriter(pipeClient) { AutoFlush = true };
                    using var reader = new StreamReader(pipeClient);
                    
                    _pipeClient = pipeClient;
                    _writer = writer;

                    // 1. Send Handshake
                    await SendPacket(MessageType.HandshakeRequest, "HELLO", token).ConfigureAwait(false);

                    retryDelayMs = AppConstants.Ipc.InitialRetryDelayMs;
                    IsConnected = true;
                    
                    // 2. Start Heartbeat
                    _ = HeartbeatLoop(token);

                    ConnectionStatusChanged?.Invoke(true);

                    while (pipeClient.IsConnected && !token.IsCancellationRequested)
                    {
                        var line = await reader.ReadLineAsync(token).ConfigureAwait(false);
                        if (line == null) 
                        {
                            FileLogger.Log("ipc_client.log", "[IPC Client] Disconnected (EOF)");
                            break;
                        }
                        
                        FileLogger.Log("ipc_client.log", $"[IPC Client] Received: {line.Substring(0, Math.Min(line.Length, 100))}");
                        HandlePacket(line);
                    }
                }
                catch (OperationCanceledException) { break; }
                catch (Exception ex)
                {
                    FileLogger.LogError("ipc_client.log", "[IPC Client] EXCEPTION", ex);
                    if (IsConnected) { IsConnected = false; ConnectionStatusChanged?.Invoke(false); }
                    int delay = Math.Clamp(retryDelayMs + _rng.Next(-AppConstants.Ipc.RetryDelayJitterMs, AppConstants.Ipc.RetryDelayJitterMs), 
                                          AppConstants.Ipc.MinRetryDelayMs, MaxRetryDelayMs);
                    Debug.WriteLine($"[IPC Client] Error: {ex.Message}. Retrying in {delay/1000.0}s");
                    if (!token.IsCancellationRequested) await Task.Delay(delay, token);
                    retryDelayMs = Math.Min(retryDelayMs * 2, MaxRetryDelayMs);
                }
                finally
                {
                    if (IsConnected) { IsConnected = false; IsHandshaked = false; ConnectionStatusChanged?.Invoke(false); }
                    _writer = null; _pipeClient = null;
                }
            }
        }

        private async Task HeartbeatLoop(CancellationToken token)
        {
            while (!token.IsCancellationRequested && IsConnected)
            {
                await Task.Delay(AppConstants.Timers.IpcHeartbeatMs, token);
                await SendPacket(MessageType.Heartbeat, string.Empty, token);
            }
        }

        private void HandlePacket(string json)
        {
            try
            {
                var packet = JsonSerializer.Deserialize<IpcPacket>(json);
                if (packet == null || packet.Version != IpcPacket.CurrentVersion) return;

                switch (packet.Type)
                {
                    case MessageType.FileActivity:
                    case MessageType.FileActivitySnapshot:
                        var activity = JsonSerializer.Deserialize<FileActivity>(packet.Payload);
                        if (activity != null)
                        {
                            // Check for duplicate events with proper thread safety
                            bool isDuplicate = false;
                            lock (_eventIdsLock)
                            {
                                if (_processedEventIds.Contains(activity.Id))
                                {
                                    isDuplicate = true;
                                }
                                else
                                {
                                    _processedEventIds.Add(activity.Id);
                                    
                                    // Trim the set if it grows too large
                                    if (_processedEventIds.Count > AppConstants.Limits.MaxProcessedEventIds)
                                    {
                                        _processedEventIds.Remove(_processedEventIds.First());
                                    }
                                }
                            }
                            
                            if (isDuplicate) return;
                            
                            // Add to activities list (separate lock to avoid holding multiple locks)
                            lock (_activitiesLock)
                            {
                                _recentActivities.Insert(0, activity);
                                if (_recentActivities.Count > MaxRecentActivities) 
                                    _recentActivities.RemoveAt(_recentActivities.Count - 1);
                            }
                            
                            FileActivityDetected?.Invoke(activity);
                        }
                        break;

                     case MessageType.ThreatDetected:
                     case MessageType.ThreatDetectedSnapshot:
                         var threat = JsonSerializer.Deserialize<Threat>(packet.Payload);
                         if (threat != null)
                         {
                             bool shouldInvoke = false;
                             lock (_threatsLock)
                             {
                                 var existing = _recentThreats.FirstOrDefault(t => string.Equals(t.Path, threat.Path, StringComparison.OrdinalIgnoreCase));
                                 if (existing != null)
                                 {
                                     // Only invoke event if status has actually changed OR if affected files count updated
                                     if (existing.ActionTaken != threat.ActionTaken || 
                                         (existing.AffectedFiles?.Count ?? 0) != (threat.AffectedFiles?.Count ?? 0))
                                     {
                                         existing.ActionTaken = threat.ActionTaken;
                                         
                                         // Update the list content rather than replacing the reference
                                         if (existing.AffectedFiles == null) existing.AffectedFiles = new List<string>();
                                         if (threat.AffectedFiles != null)
                                         {
                                             existing.AffectedFiles.Clear();
                                             existing.AffectedFiles.AddRange(threat.AffectedFiles);
                                         }
                                         
                                         shouldInvoke = true;
                                     }
                                     existing.Severity = threat.Severity;
                                     existing.Timestamp = threat.Timestamp;
                                     existing.Description = threat.Description;
                                 }
                                 else 
                                 {
                                     _recentThreats.Insert(0, threat);
                                     if (_recentThreats.Count > MaxRecentThreats)
                                         _recentThreats.RemoveAt(_recentThreats.Count - 1);
                                     shouldInvoke = true;
                                 }
                             }
                             if (shouldInvoke) ThreatDetected?.Invoke(threat);
                         }
                         break;

                    case MessageType.TelemetryUpdate:
                        var tele = JsonSerializer.Deserialize<TelemetryData>(packet.Payload);
                        if (tele != null) { lock (_telemetryLock) { _lastTelemetry = tele; } TelemetryUpdated?.Invoke(tele); }
                        break;

                    case MessageType.ProcessListUpdate:
                        var procs = JsonSerializer.Deserialize<List<ProcessInfo>>(packet.Payload);
                        if (procs != null) { lock (_processLock) { _lastProcesses = procs; } ProcessListUpdated?.Invoke(); }
                        break;
                    case MessageType.LanPeerUpdate:
                        var lanUpdate = JsonSerializer.Deserialize<LanPeerListUpdate>(packet.Payload);
                        if (lanUpdate != null) LanPeerListUpdated?.Invoke(lanUpdate);
                        break;
                    case MessageType.HandshakeResponse:
                        IsHandshaked = true;
                        IsConnected = true; // Ensure this is set
                        ConnectionStatusChanged?.Invoke(true);
                        Console.WriteLine("[IPC Client] Handshake confirmed by service.");
                        break;
                    case MessageType.Acknowledge:
                        Debug.WriteLine($"[IPC Client] Command {packet.Payload} Acknowledged.");
                        break;
                }
            }
            catch (Exception ex)
            {
                FileLogger.LogError("ipc_client.log", "[IPC Client] Packet handle error", ex);
            }
        }

        private async Task SendPacket(MessageType type, object data, CancellationToken token = default)
        {
            if (_disposed) return;
            
            await _writeSemaphore.WaitAsync(token).ConfigureAwait(false);
            try
            {
                // Double-check disposal after acquiring semaphore
                if (_disposed) return;
                
                // Capture references inside the lock to prevent race conditions with Dispose()
                var writer = _writer;
                var pipe = _pipeClient;
                
                // Check if disposed or disconnected after acquiring the semaphore
                if (writer == null || pipe?.IsConnected != true)
                    return;
                
                var packet = new IpcPacket
                {
                    Type = type,
                    SequenceId = Interlocked.Increment(ref _nextSequenceId),
                    Payload = JsonSerializer.Serialize(data)
                };
                
                // Use null-conditional operator for additional safety
                // This prevents NullReferenceException if Dispose() is called between check and use
                var json = JsonSerializer.Serialize(packet);
                if (writer != null && !_disposed)
                {
                    await writer.WriteLineAsync(json).ConfigureAwait(false);
                }
            }
            catch (ObjectDisposedException)
            {
                // Expected during shutdown - ignore
            }
            catch (IOException ex)
            {
                // Pipe disconnected - log but don't crash
                Debug.WriteLine($"[IPC Client] SendPacket IO error: {ex.Message}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[IPC Client] SendPacket error: {ex.Message}");
            }
            finally
            {
                try
                {
                    _writeSemaphore.Release();
                }
                catch (ObjectDisposedException)
                {
                    // Semaphore was disposed - ignore during shutdown
                }
            }
        }

        private async Task SendCommand(CommandType command, string args = "")
        {
            var request = new CommandRequest { Command = command, Arguments = args };
            await SendPacket(MessageType.CommandRequest, request).ConfigureAwait(false);
        }

        public IEnumerable<Threat> GetRecentThreats() { lock (_threatsLock) { return _recentThreats.ToList(); } }
        public IEnumerable<FileActivity> GetRecentFileActivities() { lock (_activitiesLock) { return _recentActivities.ToList(); } }
        public IEnumerable<ProcessInfo> GetActiveProcesses() 
        { 
            if (!IsConnected) return Enumerable.Empty<ProcessInfo>();
            lock (_processLock) { return _lastProcesses.ToList(); } 
        }
        


        public double GetSystemCpuUsage() { lock (_telemetryLock) { return _lastTelemetry.CpuUsage; } }
        public long GetSystemMemoryUsage() { lock (_telemetryLock) { return _lastTelemetry.MemoryUsage; } }
        public int GetMonitoredFilesCount() { return ConfigurationService.Instance.MonitoredPaths.Count; }
        public TelemetryData GetTelemetry() { lock (_telemetryLock) { return _lastTelemetry; } }
        public double GetQuarantineStorageUsage() { lock (_telemetryLock) { return _lastTelemetry.QuarantineStorageMb; } }

        public IEnumerable<string> GetQuarantinedFiles()
        {
            try { return Directory.GetFiles(PathConfiguration.QuarantinePath, "*.quarantine"); }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ServicePipeClient] GetQuarantinedFiles failed: {ex.Message}");
                return Enumerable.Empty<string>();
            }
        }

        public async Task KillProcess(int pid) => await SendCommand(CommandType.KillProcess, pid.ToString());
        public void InitializeWatchers() => _ = SendCommand(CommandType.UpdatePaths);

        public async Task QuarantineFile(string filePath)
        {
            if (IsConnected) 
            {
                await SendCommand(CommandType.QuarantineFile, filePath).ConfigureAwait(false);
            }
            
            lock (_threatsLock)
            {
                var matching = _recentThreats.Where(t => string.Equals(t.Path, filePath, StringComparison.OrdinalIgnoreCase));
                foreach (var t in matching) t.ActionTaken = "Quarantined";
            }
        }

        public async Task RestoreQuarantinedFile(string path) 
        { 
            if (IsConnected) 
                await SendCommand(CommandType.RestoreFile, path).ConfigureAwait(false); 
        }
        
        public async Task DeleteQuarantinedFile(string path) 
        { 
            if (IsConnected) 
                await SendCommand(CommandType.DeleteFile, path).ConfigureAwait(false); 
        }
        
        public async Task ClearSafeFiles() 
        { 
            if (IsConnected) 
                await SendCommand(CommandType.ClearSafeFiles).ConfigureAwait(false); 
        }
        
        public async Task WhitelistProcess(string name) 
        { 
            if (IsConnected) 
                await SendCommand(CommandType.WhitelistProcess, name).ConfigureAwait(false); 
        }
        
        public async Task RemoveWhitelist(string name) 
        { 
            if (IsConnected) 
                await SendCommand(CommandType.RemoveWhitelist, name).ConfigureAwait(false); 
        }
        
        public async Task MitigateThreat(string threatId)
        {
            if (IsConnected)
                await SendCommand(CommandType.MitigateThreat, threatId).ConfigureAwait(false);
        }

        public async Task HandleMassEncryptionResponse(int processId, string processName, List<string> filesToQuarantine)
        {
            if (IsConnected)
            {
                var payload = new
                {
                    ProcessId = processId,
                    ProcessName = processName,
                    FilesToQuarantine = filesToQuarantine
                };
                await SendCommand(CommandType.HandleMassEncryption, JsonSerializer.Serialize(payload)).ConfigureAwait(false);
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            
            _cts?.Cancel();
            
            // Wait for any pending writes to complete, but don't block indefinitely
            bool acquired = false;
            try
            {
                // Use shorter timeout to prevent hanging during shutdown
                acquired = _writeSemaphore.Wait(AppConstants.Ipc.DisposalSemaphoreTimeoutMs);
                if (!acquired)
                {
                    Debug.WriteLine("[IPC Client] Warning: Semaphore timeout during disposal - forcing cleanup");
                    // Force cleanup even if semaphore not acquired
                }
                
                _writer?.Dispose();
                _pipeClient?.Dispose();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[IPC Client] Dispose error: {ex.Message}");
            }
            finally
            {
                // Only release if we successfully acquired the semaphore
                if (acquired)
                {
                    try 
                    { 
                        _writeSemaphore.Release(); 
                    }
                    catch (SemaphoreFullException)
                    {
                        // Semaphore was already released - this is fine during disposal
                        Debug.WriteLine("[IPC Client] Semaphore already released during disposal");
                    }
                    catch (ObjectDisposedException)
                    {
                        // Semaphore was disposed - ignore
                    }
                }
                
                _cts?.Dispose();
                _writeSemaphore.Dispose();
            }
        }
    }
}
