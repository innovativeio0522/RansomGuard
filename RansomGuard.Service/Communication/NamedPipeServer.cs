using System;
using System.Collections.Concurrent;
using System.IO;
using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Linq;
using RansomGuard.Core.IPC;
using RansomGuard.Core.Interfaces;
using RansomGuard.Core.Models;
using RansomGuard.Core.Helpers;
using RansomGuard.Core.Configuration;
using RansomGuard.Service.Engine;

namespace RansomGuard.Service.Communication
{
    public class NamedPipeServer
    {
        private readonly string _pipeName;
        private readonly ISystemMonitorService _monitorService;
        private readonly ITelemetryService _telemetryService;
        private CancellationTokenSource? _cts;

        private class ClientContext
        {
            public Guid Id { get; } = Guid.NewGuid();
            public StreamWriter Writer { get; }
            public BlockingCollection<string> MessageQueue { get; } = new(AppConstants.Ipc.ClientMessageQueueSize);
            public Task ProcessorTask { get; set; } = Task.CompletedTask;
            public bool IsHandshaked { get; set; }
            public DateTime LastHeartbeat { get; set; } = DateTime.Now;

            public ClientContext(StreamWriter writer)
            {
                Writer = writer;
            }
        }

        private readonly ConcurrentDictionary<Guid, ClientContext> _clients = new();

        public NamedPipeServer(ISystemMonitorService monitorService, ITelemetryService telemetryService, string pipeName = "SentinelGuardPipe")
        {
            _monitorService = monitorService;
            _telemetryService = telemetryService;
            _pipeName = pipeName;
        }

        public void Start()
        {
            _cts = new CancellationTokenSource();
            Task.Run(() => ListenLoop(_cts.Token));
            Task.Run(() => TelemetryBroadcastLoop(_cts.Token));
            Task.Run(() => ProcessListBroadcastLoop(_cts.Token));
            Task.Run(() => HeartbeatMonitorLoop(_cts.Token));

            _monitorService.FileActivityDetected += (activity) => ReliableBroadcast(MessageType.FileActivity, activity);
            _monitorService.ThreatDetected += (threat) => ReliableBroadcast(MessageType.ThreatDetected, threat);

        }

        private async Task TelemetryBroadcastLoop(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    var telemetry = _monitorService.GetTelemetry();
                    telemetry.ActiveEndpointsCount = _clients.Count;
                    // Telemetry is lossy — we use DropOldest strategy
                    Broadcast(MessageType.TelemetryUpdate, telemetry, dropOldest: true);
                }
                catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"TelemetryBroadcastLoop error: {ex.Message}"); }
                await Task.Delay(AppConstants.Timers.TelemetryCollectionMs, token).ConfigureAwait(false);
            }
        }

        private async Task ProcessListBroadcastLoop(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    var processes = _monitorService.GetActiveProcesses();
                    Broadcast(MessageType.ProcessListUpdate, processes, dropOldest: true);
                }
                catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"ProcessListBroadcastLoop error: {ex.Message}"); }
                await Task.Delay(AppConstants.Timers.ProcessListBroadcastMs, token).ConfigureAwait(false);
            }
        }

        private async Task HeartbeatMonitorLoop(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                var now = DateTime.Now;
                foreach (var client in _clients.Values)
                {
                    if ((now - client.LastHeartbeat).TotalSeconds > AppConstants.Ipc.ClientHeartbeatTimeoutSeconds)
                    {
                        System.Diagnostics.Debug.WriteLine($"[IPC] Client {client.Id} timed out. Removing.");
                        client.MessageQueue.CompleteAdding();
                        try { client.Writer.Dispose(); } catch { }
                        _clients.TryRemove(client.Id, out _);
                    }
                }
                await Task.Delay(AppConstants.Timers.HeartbeatMonitorMs, token).ConfigureAwait(false);
            }
        }

        public void Stop()
        {
            _cts?.Cancel();
            foreach (var (id, ctx) in _clients)
            {
                try { ctx.MessageQueue.CompleteAdding(); ctx.Writer.Dispose(); }
                catch { }
            }
            _clients.Clear();
        }

        private async Task ListenLoop(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                NamedPipeServerStream? pipeServer = null;
                NamedPipeServerStream? clientPipe = null;
                try
                {
                    var pipeSecurity = new PipeSecurity();
                    
                    // Restrict access to authenticated users and system to prevent unauthorized access/spoofing
                    pipeSecurity.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null), PipeAccessRights.FullControl, AccessControlType.Allow));
                    pipeSecurity.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.AuthenticatedUserSid, null), PipeAccessRights.ReadWrite, AccessControlType.Allow));
                    pipeSecurity.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null), PipeAccessRights.FullControl, AccessControlType.Allow));

                    FileLogger.Log("ipc.log", $"[IPC Server] Creating pipe: {_pipeName}");
                    pipeServer = NamedPipeServerStreamAcl.Create(_pipeName, PipeDirection.InOut, NamedPipeServerStream.MaxAllowedServerInstances, PipeTransmissionMode.Byte, PipeOptions.Asynchronous, 0, 0, pipeSecurity);

                    FileLogger.Log("ipc.log", "[IPC Server] Waiting for connection...");
                    await pipeServer.WaitForConnectionAsync(token).ConfigureAwait(false);
                    FileLogger.Log("ipc.log", "[IPC Server] Client connected!");
                    
                    // Transfer ownership to clientPipe
                    clientPipe = pipeServer;
                    pipeServer = null;
                    
                    // Capture clientPipe in local variable for the task
                    var capturedPipe = clientPipe;
                    clientPipe = null; // Ownership transferred to task
                    
                    _ = Task.Run(async () => {
                        try
                        {
                            // In .NET 8, we can get the client PID from the PipeSecurity or via impersonation
                            // For now, let's just log that a new connection is established
                            FileLogger.Log("ipc_connections.log", $"[NamedPipeServer] New connection established. Active clients: {_clients.Count}");
                        }
                        catch { }
                        try { await HandleClient(capturedPipe, token).ConfigureAwait(false); }
                        catch (Exception ex) { 
                            FileLogger.LogError("ipc.log", "[IPC Server] HandleClient error", ex);
                        }
                        finally { capturedPipe.Dispose(); }
                    }, token);
                }
                catch (Exception ex) 
                { 
                    FileLogger.LogError("ipc.log", "[IPC Server] EXCEPTION in ListenLoop", ex);
                    
                    // Cleanup: dispose whichever pipe still has ownership
                    pipeServer?.Dispose();
                    clientPipe?.Dispose();
                    
                    if (!token.IsCancellationRequested) await Task.Delay(AppConstants.Ipc.PipeReconnectDelayMs, token).ConfigureAwait(false); 
                }
            }
        }

        private async Task HandleClient(NamedPipeServerStream pipe, CancellationToken token)
        {
            using var reader = new StreamReader(pipe);
            using var writer = new StreamWriter(pipe) { AutoFlush = true };

            var context = new ClientContext(writer);
            context.ProcessorTask = Task.Run(() => ProcessOutgoingMessages(context, token));
            _clients[context.Id] = context;

            FileLogger.Log("ipc.log", $"[IPC Server] Client connected. ID: {context.Id}");

            try
            {
                while (pipe.IsConnected && !token.IsCancellationRequested)
                {
                    var line = await reader.ReadLineAsync(token).ConfigureAwait(false);
                    if (line == null) 
                    {
                        FileLogger.Log("ipc.log", $"[IPC Server] Client {context.Id} disconnected (EOF).");
                        break;
                    }

                    FileLogger.Log("ipc.log", $"[IPC Server] Received from {context.Id}: {line.Substring(0, Math.Min(line.Length, 100))}");

                    try
                    {
                        var packet = JsonSerializer.Deserialize<IpcPacket>(line);
                        if (packet == null) 
                        {
                            FileLogger.LogWarning("ipc.log", $"[IPC Server] Failed to deserialize packet from client {context.Id}");
                            continue;
                        }

                        context.LastHeartbeat = DateTime.Now;

                        if (packet.Type == MessageType.HandshakeRequest)
                        {
                            FileLogger.Log("ipc.log", $"[IPC Server] Handshake received from client {context.Id}");
                            context.IsHandshaked = true;
                            EnqueueMessage(context, MessageType.HandshakeResponse, "READY");
                            
                            // Send Snapshots AFTER handshake
                            foreach (var activity in _monitorService.GetRecentFileActivities())
                                EnqueueMessage(context, MessageType.FileActivitySnapshot, activity);

                            foreach (var threat in _monitorService.GetRecentThreats())
                                EnqueueMessage(context, MessageType.ThreatDetectedSnapshot, threat);

                            EnqueueMessage(context, MessageType.TelemetryUpdate, _monitorService.GetTelemetry());
                        }
                        else if (packet.Type == MessageType.Heartbeat)
                        {
                            // Already updated LastHeartbeat above
                        }
                        else if (packet.Type == MessageType.CommandRequest)
                        {
                            var request = JsonSerializer.Deserialize<CommandRequest>(packet.Payload);
                            // ACK the command immediately
                            EnqueueMessage(context, MessageType.Acknowledge, packet.SequenceId);
                            await HandleCommand(request, writer).ConfigureAwait(false);
                        }
                    }
                    catch (JsonException ex)
                    {
                        FileLogger.LogError("ipc.log", $"[IPC Server] JSON deserialization error from client {context.Id}", ex);
                    }
                    catch (Exception ex)
                    {
                        FileLogger.LogError("ipc.log", $"[IPC Server] Packet processing error from client {context.Id}", ex);
                    }
                }
            }
            catch (IOException ex)
            {
                FileLogger.LogError("ipc.log", $"[IPC Server] IO error with client {context.Id}", ex);
            }
            catch (OperationCanceledException)
            {
                FileLogger.Log("ipc.log", $"[IPC Server] Client {context.Id} operation cancelled");
            }
            catch (Exception ex)
            {
                FileLogger.LogError("ipc.log", $"[IPC Server] Unexpected error with client {context.Id}", ex);
            }
            finally
            {
                context.MessageQueue.CompleteAdding();
                _clients.TryRemove(context.Id, out _);
                FileLogger.Log("ipc.log", $"[IPC Server] Client {context.Id} disconnected and cleaned up");
            }
        }

        private void ProcessOutgoingMessages(ClientContext context, CancellationToken token)
        {
            try
            {
                foreach (var message in context.MessageQueue.GetConsumingEnumerable(token))
                {
                    try
                    {
                        context.Writer.WriteLine(message);
                    }
                    catch (Exception ex)
                    {
                        FileLogger.LogWarning("ipc.log", $"[IPC Server] Write error for client {context.Id}. Disconnecting. {ex.Message}");
                        try { context.Writer.Dispose(); } catch { }
                        break;
                    }
                }
            }
            catch { }
        }

        private async Task HandleCommand(CommandRequest? request, StreamWriter writer)
        {
            if (request == null) 
            {
                FileLogger.LogWarning("ipc.log", "[IPC Server] HandleCommand received null request");
                return;
            }

            // Validate arguments for commands that require them
            bool requiresArguments = request.Command != CommandType.UpdatePaths && 
                                    request.Command != CommandType.ClearSafeFiles;
            
            if (requiresArguments && string.IsNullOrWhiteSpace(request.Arguments))
            {
                FileLogger.LogWarning("ipc.log", $"[IPC Server] Command {request.Command} requires arguments but none provided");
                return;
            }

            try
            {
                switch (request.Command)
                {
                    case CommandType.KillProcess:
                        if (int.TryParse(request.Arguments, out int pid))
                        {
                            await _monitorService.KillProcess(pid).ConfigureAwait(false);
                            FileLogger.Log("ipc.log", $"[IPC Server] Killed process PID: {pid}");
                        }
                        else
                        {
                            FileLogger.LogWarning("ipc.log", $"[IPC Server] Invalid PID argument: {request.Arguments}");
                        }
                        break;

                    case CommandType.QuarantineFile:
                        if (!string.IsNullOrEmpty(request.Arguments))
                        {
                            await _monitorService.QuarantineFile(request.Arguments).ConfigureAwait(false);
                            var updatedThreats = _monitorService.GetRecentThreats().Where(t => string.Equals(t.Path, request.Arguments, StringComparison.OrdinalIgnoreCase));
                            foreach (var t in updatedThreats) ReliableBroadcast(MessageType.ThreatDetected, t);
                            FileLogger.Log("ipc.log", $"[IPC Server] Quarantined file: {request.Arguments}");
                        }
                        break;

                    case CommandType.UpdatePaths:
                        _monitorService.InitializeWatchers();
                        FileLogger.Log("ipc.log", "[IPC Server] Updated monitored paths");
                        break;

                    case CommandType.RestoreFile:
                        if (!string.IsNullOrEmpty(request.Arguments))
                        {
                            await _monitorService.RestoreQuarantinedFile(request.Arguments).ConfigureAwait(false);
                            FileLogger.Log("ipc.log", $"[IPC Server] Restored file: {request.Arguments}");
                        }
                        break;

                    case CommandType.DeleteFile:
                        if (!string.IsNullOrEmpty(request.Arguments))
                        {
                            await _monitorService.DeleteQuarantinedFile(request.Arguments).ConfigureAwait(false);
                            FileLogger.Log("ipc.log", $"[IPC Server] Deleted quarantined file: {request.Arguments}");
                        }
                        break;

                    case CommandType.ClearSafeFiles:
                        await _monitorService.ClearSafeFiles().ConfigureAwait(false);
                        FileLogger.Log("ipc.log", "[IPC Server] Cleared safe files");
                        break;

                    case CommandType.WhitelistProcess:
                        if (!string.IsNullOrEmpty(request.Arguments))
                        {
                            await _monitorService.WhitelistProcess(request.Arguments).ConfigureAwait(false);
                            FileLogger.Log("ipc.log", $"[IPC Server] Whitelisted process: {request.Arguments}");
                        }
                        break;

                    case CommandType.RemoveWhitelist:
                        if (!string.IsNullOrEmpty(request.Arguments))
                        {
                            await _monitorService.RemoveWhitelist(request.Arguments).ConfigureAwait(false);
                            FileLogger.Log("ipc.log", $"[IPC Server] Removed whitelist: {request.Arguments}");
                        }
                        break;
                    case CommandType.MitigateThreat:
                        if (!string.IsNullOrEmpty(request.Arguments))
                        {
                            await _monitorService.MitigateThreat(request.Arguments).ConfigureAwait(false);
                            FileLogger.Log("ipc.log", $"[IPC Server] Mitigated threat ID: {request.Arguments}");
                        }
                        break;
                    
                    case CommandType.HandleMassEncryption:
                        if (!string.IsNullOrEmpty(request.Arguments))
                        {
                            try
                            {
                                var payload = System.Text.Json.JsonSerializer.Deserialize<MassEncryptionPayload>(request.Arguments);
                                if (payload != null)
                                {
                                    await _monitorService.HandleMassEncryptionResponse(
                                        payload.ProcessId, 
                                        payload.ProcessName, 
                                        payload.FilesToQuarantine).ConfigureAwait(false);
                                    FileLogger.Log("ipc.log", $"[IPC Server] Handled mass encryption response for process: {payload.ProcessName} (PID: {payload.ProcessId})");
                                }
                            }
                            catch (Exception ex)
                            {
                                FileLogger.LogError("ipc.log", "[IPC Server] Failed to deserialize mass encryption payload", ex);
                            }
                        }
                        break;

                    default:
                        FileLogger.LogWarning("ipc.log", $"[IPC Server] Unknown command type: {request.Command}");
                        break;
                }
            }
            catch (Exception ex)
            {
                FileLogger.LogError("ipc.log", $"[IPC Server] Error executing command {request.Command}", ex);
            }
        }

        private class MassEncryptionPayload
        {
            public int ProcessId { get; set; }
            public string ProcessName { get; set; } = string.Empty;
            public List<string> FilesToQuarantine { get; set; } = new();
        }

        private void ReliableBroadcast<T>(MessageType type, T data) => Broadcast(type, data, dropOldest: false);

        public void Broadcast<T>(MessageType type, T data, bool dropOldest)
        {
            var packet = new IpcPacket { Type = type, Payload = JsonSerializer.Serialize(data) };
            var json = JsonSerializer.Serialize(packet);

            foreach (var ctx in _clients.Values)
            {
                if (!ctx.IsHandshaked && type != MessageType.HandshakeResponse) continue;

                if (!ctx.MessageQueue.IsAddingCompleted)
                {
                    // CRITICAL: If this is a threat, or if dropOldest is requested, 
                    // and we are over the high water mark, make room by dropping the oldest message.
                    if ((dropOldest || type == MessageType.ThreatDetected) && 
                        ctx.MessageQueue.Count >= AppConstants.Ipc.MessageQueueHighWaterMark)
                    {
                        // Drop oldest to avoid blocking the broadcast loop
                        ctx.MessageQueue.TryTake(out _);
                    }

                    if (!ctx.MessageQueue.TryAdd(json))
                    {
                        // If it's a threat and we still failed to add (rare with the logic above), 
                        // log a severe error.
                        if (type == MessageType.ThreatDetected)
                        {
                            FileLogger.LogError("ipc.log", $"[IPC Server] CRITICAL FAILURE: Could not enqueue THREAT ALERT to client {ctx.Id}.");
                        }
                        else
                        {
                            FileLogger.LogWarning("ipc.log", $"[IPC Server] Failed to enqueue message of type {type} to client {ctx.Id}. Queue full.");
                        }
                    }
                }
            }
        }

        private void EnqueueMessage<T>(ClientContext context, MessageType type, T data)
        {
            var packet = new IpcPacket { Type = type, Payload = JsonSerializer.Serialize(data) };
            context.MessageQueue.TryAdd(JsonSerializer.Serialize(packet));
        }
    }
}
