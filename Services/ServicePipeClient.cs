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

namespace RansomGuard.Services
{
    public class ServicePipeClient : ISystemMonitorService, IDisposable
    {
        private readonly string _pipeName;
        private const int MaxRecentActivities = 200;
        
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

        public ServicePipeClient(string pipeName = "SentinelGuardPipe")
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
            int retryDelayMs = 2000;
            const int MaxRetryDelayMs = 30_000;

            while (!token.IsCancellationRequested)
            {
                try
                {
                    using var pipeClient = new NamedPipeClientStream(".", _pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
                    File.AppendAllText(@"C:\ProgramData\RansomGuard\Logs\ipc_client.log", $"{DateTime.Now}: [IPC Client] Attempting to connect to: {_pipeName}\n");
                    await pipeClient.ConnectAsync(5000, token);
                    File.AppendAllText(@"C:\ProgramData\RansomGuard\Logs\ipc_client.log", $"{DateTime.Now}: [IPC Client] Connected to pipe server!\n");
                    
                    using var writer = new StreamWriter(pipeClient) { AutoFlush = true };
                    using var reader = new StreamReader(pipeClient);
                    
                    _pipeClient = pipeClient;
                    _writer = writer;

                    // 1. Send Handshake
                    await SendPacket(MessageType.HandshakeRequest, "HELLO", token).ConfigureAwait(false);

                    // 2. Start Heartbeat
                    _ = HeartbeatLoop(token);

                    retryDelayMs = 2000;
                    IsConnected = true;
                    
                    lock (_activitiesLock) { _recentActivities.Clear(); }
                    lock (_threatsLock) { _recentThreats.Clear(); }
                    ConnectionStatusChanged?.Invoke(true);

                    while (pipeClient.IsConnected && !token.IsCancellationRequested)
                    {
                        var line = await reader.ReadLineAsync(token).ConfigureAwait(false);
                        if (line == null) 
                        {
                            LogToFile("[IPC Client] Disconnected (EOF)");
                            break;
                        }
                        
                        LogToFile($"[IPC Client] Received: {line.Substring(0, Math.Min(line.Length, 100))}");
                        HandlePacket(line);
                    }
                }
                catch (OperationCanceledException) { break; }
                catch (Exception ex)
                {
                    LogToFile($"[IPC Client] EXCEPTION: {ex.Message}\n{ex.StackTrace}");
                    if (IsConnected) { IsConnected = false; ConnectionStatusChanged?.Invoke(false); }
                    int delay = Math.Clamp(retryDelayMs + _rng.Next(-200, 200), 1000, MaxRetryDelayMs);
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
                await Task.Delay(10000, token);
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
                            lock (_activitiesLock)
                            {
                                if (_processedEventIds.Contains(activity.Id)) return;
                                _processedEventIds.Add(activity.Id);
                                if (_processedEventIds.Count > 1000) _processedEventIds.Remove(_processedEventIds.First());

                                _recentActivities.Insert(0, activity);
                                if (_recentActivities.Count > MaxRecentActivities) _recentActivities.RemoveAt(_recentActivities.Count - 1);
                            }
                            FileActivityDetected?.Invoke(activity);
                        }
                        break;

                     case MessageType.ThreatDetected:
                     case MessageType.ThreatDetectedSnapshot:
                         var threat = JsonSerializer.Deserialize<Threat>(packet.Payload);
                         if (threat != null)
                         {
                             lock (_threatsLock)
                             {
                                 var existing = _recentThreats.FirstOrDefault(t => string.Equals(t.Path, threat.Path, StringComparison.OrdinalIgnoreCase));
                                 if (existing != null)
                                 {
                                     if (existing.ActionTaken != "Quarantined" && existing.ActionTaken != "Ignored") existing.ActionTaken = threat.ActionTaken;
                                     existing.Severity = threat.Severity;
                                     existing.Timestamp = threat.Timestamp;
                                 }
                                 else _recentThreats.Insert(0, threat);
                             }
                             ThreatDetected?.Invoke(threat);
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
                    case MessageType.HandshakeResponse:
                        IsHandshaked = true;
                        Console.WriteLine("[IPC Client] Handshake confirmed by service.");
                        break;
                    case MessageType.Acknowledge:
                        Debug.WriteLine($"[IPC Client] Command {packet.Payload} Acknowledged.");
                        break;
                }
            }
            catch (Exception ex)
            {
                LogToFile($"[IPC Client] Packet handle error: {ex.Message}\n{ex.StackTrace}");
            }
        }

        private async Task SendPacket(MessageType type, object data, CancellationToken token = default)
        {
            if (_writer == null || _pipeClient?.IsConnected != true || _disposed) return;
            
            await _writeSemaphore.WaitAsync(token).ConfigureAwait(false);
            try
            {
                if (_writer == null || _disposed) return;
                
                var packet = new IpcPacket
                {
                    Type = type,
                    SequenceId = Interlocked.Increment(ref _nextSequenceId),
                    Payload = JsonSerializer.Serialize(data)
                };
                await _writer.WriteLineAsync(JsonSerializer.Serialize(packet)).ConfigureAwait(false);
            }
            finally
            {
                _writeSemaphore.Release();
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
            catch { return Enumerable.Empty<string>(); }
        }

        public async Task KillProcess(int pid) => await SendCommand(CommandType.KillProcess, pid.ToString());
        public void InitializeWatchers() => _ = SendCommand(CommandType.UpdatePaths);

        public async Task QuarantineFile(string filePath)
        {
            if (IsConnected) await SendCommand(CommandType.QuarantineFile, filePath);
            lock (_threatsLock)
            {
                var matching = _recentThreats.Where(t => string.Equals(t.Path, filePath, StringComparison.OrdinalIgnoreCase));
                foreach (var t in matching) t.ActionTaken = "Quarantined";
            }
        }

        public async Task RestoreQuarantinedFile(string path) { if (IsConnected) await SendCommand(CommandType.RestoreFile, path); }
        public async Task DeleteQuarantinedFile(string path) { if (IsConnected) await SendCommand(CommandType.DeleteFile, path); }
        public async Task ClearSafeFiles() { if (IsConnected) await SendCommand(CommandType.ClearSafeFiles); }
        public async Task WhitelistProcess(string name) => await SendCommand(CommandType.WhitelistProcess, name);
        public async Task RemoveWhitelist(string name) => await SendCommand(CommandType.RemoveWhitelist, name);

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            
            _cts?.Cancel();
            
            // Try to acquire semaphore to ensure no writes are in progress, 
            // but don't block indefinitely during disposal
            _writeSemaphore.Wait(100); 
            
            try
            {
                _writer?.Dispose();
                _pipeClient?.Dispose();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[IPC Client] Dispose error: {ex.Message}");
            }
            finally
            {
                _cts?.Dispose();
                _writeSemaphore.Dispose();
            }
        }
        private void LogToFile(string message)
        {
            try
            {
                string logPath = @"C:\ProgramData\RansomGuard\Logs\ipc_client.log";
                string dir = Path.GetDirectoryName(logPath)!;
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

                using (var fs = new FileStream(logPath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite))
                using (var sw = new StreamWriter(fs))
                {
                    sw.WriteLine($"{DateTime.Now}: {message}");
                }
            }
            catch { }
        }
    }
}
