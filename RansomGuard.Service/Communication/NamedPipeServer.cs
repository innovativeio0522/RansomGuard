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
using RansomGuard.Core.Constants;
using System.Text;
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
            public DateTime LastRequestTime { get; set; } = DateTime.MinValue;

            public ClientContext(StreamWriter writer)
            {
                Writer = writer;
            }
        }

        private readonly ConcurrentDictionary<Guid, ClientContext> _clients = new();
        private readonly object _clientsLock = new();
        private const int MaxClients = 10; // Match the pipe's maxNumberOfServerInstances

        public NamedPipeServer(ISystemMonitorService monitorService, ITelemetryService telemetryService, string? pipeName = null)
        {
            _monitorService = monitorService;
            _telemetryService = telemetryService;
            _pipeName = pipeName ?? AppIdentifiers.PipeName;
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
                // Snapshot the keys first — avoids enumerating while HandleClient may be adding/removing
                var clientIds = _clients.Keys.ToArray();
                foreach (var id in clientIds)
                {
                    if (!_clients.TryGetValue(id, out var client)) continue;

                    if ((now - client.LastHeartbeat).TotalSeconds > AppConstants.Ipc.ClientHeartbeatTimeoutSeconds)
                    {
                        System.Diagnostics.Debug.WriteLine($"[IPC] Client {client.Id} timed out. Removing.");
                        // Mark queue as complete first so ProcessOutgoingMessages exits cleanly
                        if (!client.MessageQueue.IsAddingCompleted)
                            client.MessageQueue.CompleteAdding();
                        try { client.Writer.Dispose(); } catch { }
                        // Only remove if HandleClient hasn't already removed it
                        _clients.TryRemove(id, out _);
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

        internal static PipeSecurity CreatePipeSecurity()
        {
            var pipeSecurity = new PipeSecurity();

            var localSystemSid = new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null);
            pipeSecurity.AddAccessRule(new PipeAccessRule(
                localSystemSid,
                PipeAccessRights.FullControl,
                AccessControlType.Allow));

            var administratorsSid = new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null);
            pipeSecurity.AddAccessRule(new PipeAccessRule(
                administratorsSid,
                PipeAccessRights.FullControl,
                AccessControlType.Allow));

            var authenticatedUsersSid = new SecurityIdentifier(WellKnownSidType.AuthenticatedUserSid, null);
            pipeSecurity.AddAccessRule(new PipeAccessRule(
                authenticatedUsersSid,
                PipeAccessRights.ReadWrite,
                AccessControlType.Allow));

            return pipeSecurity;
        }

        private async Task ListenLoop(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                NamedPipeServerStream? pipeServer = null;
                NamedPipeServerStream? clientPipe = null;
                try
                {
                    FileLogger.Log(AppIdentifiers.IpcLogFile, $"[IPC Server] Creating pipe: {_pipeName}");
                    var pipeSecurity = CreatePipeSecurity();
                    
                    pipeServer = NamedPipeServerStreamAcl.Create(_pipeName, PipeDirection.InOut, 10, PipeTransmissionMode.Byte, PipeOptions.Asynchronous, 4096, 4096, pipeSecurity);
                    
                    FileLogger.Log(AppIdentifiers.IpcLogFile, "[IPC Server] Pipe created with restricted authenticated-user access");

                    FileLogger.Log(AppIdentifiers.IpcLogFile, "[IPC Server] Waiting for connection...");
                    await pipeServer.WaitForConnectionAsync(token).ConfigureAwait(false);
                    FileLogger.Log(AppIdentifiers.IpcLogFile, "[IPC Server] Client connected!");
                    
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
                            FileLogger.Log(AppIdentifiers.IpcConnectionsLogFile, $"[NamedPipeServer] New connection established. Active clients: {_clients.Count}");
                        }
                        catch { }
                        try { await HandleClient(capturedPipe, token).ConfigureAwait(false); }
                        catch (Exception ex) { 
                            FileLogger.LogError(AppIdentifiers.IpcLogFile, "[IPC Server] HandleClient error", ex);
                        }
                        finally { capturedPipe.Dispose(); }
                    }, token).ContinueWith(task =>
                    {
                        if (task.IsFaulted && task.Exception != null)
                        {
                            FileLogger.LogError(AppIdentifiers.IpcLogFile, "[IPC Server] Client handler task failed", task.Exception);
                        }
                    }, TaskScheduler.Default);
                }
                catch (Exception ex) 
                { 
                    FileLogger.LogError(AppIdentifiers.IpcLogFile, "[IPC Server] EXCEPTION in ListenLoop", ex);
                    
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
            context.ProcessorTask = Task.Run(() => ProcessOutgoingMessagesAsync(context, token), token);

            lock (_clientsLock)
            {
                if (_clients.Count >= MaxClients)
                {
                    FileLogger.LogWarning(AppIdentifiers.IpcLogFile, $"[IPC Server] Max client limit ({MaxClients}) reached. Rejecting new connection.");
                    return;
                }

                _clients[context.Id] = context;
            }

            FileLogger.Log(AppIdentifiers.IpcLogFile, $"[IPC Server] Client connected. ID: {context.Id}. Active clients: {_clients.Count}");

            try
            {
                while (pipe.IsConnected && !token.IsCancellationRequested)
                {
                    var line = await ReadLimitedLineAsync(reader, AppConstants.Ipc.MaxIpcPayloadSizeBytes + 1024, token).ConfigureAwait(false);
                    if (line == null) 
                    {
                        FileLogger.Log(AppIdentifiers.IpcLogFile, $"[IPC Server] Client {context.Id} disconnected (EOF).");
                        break;
                    }

                    // SECURITY: Validate payload size
                    if (line.Length > AppConstants.Ipc.MaxIpcPayloadSizeBytes)
                    {
                        FileLogger.LogWarning(AppIdentifiers.IpcLogFile, $"[IPC Server] Rejecting oversized payload ({line.Length} bytes) from client {context.Id}");
                        EnqueueMessage(context, MessageType.Acknowledge, "ERROR: PAYLOAD_TOO_LARGE");
                        continue;
                    }

                    // SECURITY: Implement basic request throttling
                    var now = DateTime.Now;
                    if ((now - context.LastRequestTime).TotalMilliseconds < AppConstants.Ipc.IpcRequestThrottleMs)
                    {
                        // Too many requests — silent ignore or error? Let's ignore for now to avoid filling pipe
                        continue;
                    }
                    context.LastRequestTime = now;

                    FileLogger.Log(AppIdentifiers.IpcLogFile, $"[IPC Server] Received from {context.Id}: {line.Substring(0, Math.Min(line.Length, 100))}");

                    try
                    {
                        var packet = JsonSerializer.Deserialize<IpcPacket>(line);
                        if (packet == null) 
                        {
                            FileLogger.LogWarning(AppIdentifiers.IpcLogFile, $"[IPC Server] Failed to deserialize packet from client {context.Id}");
                            continue;
                        }

                        context.LastHeartbeat = DateTime.Now;

                        if (packet.Type == MessageType.HandshakeRequest)
                        {
                            FileLogger.Log(AppIdentifiers.IpcLogFile, $"[IPC Server] Handshake received from client {context.Id}");
                            context.IsHandshaked = true;
                            EnqueueMessage(context, MessageType.HandshakeResponse, "READY");
                            
                            // Send Snapshots AFTER handshake
                            try
                            {
                                foreach (var activity in _monitorService.GetRecentFileActivities() ?? Enumerable.Empty<FileActivity>())
                                    EnqueueMessage(context, MessageType.FileActivitySnapshot, activity);

                                foreach (var threat in _monitorService.GetRecentThreats() ?? Enumerable.Empty<Threat>())
                                    EnqueueMessage(context, MessageType.ThreatDetectedSnapshot, threat);

                                var telemetryData = _monitorService.GetTelemetry();
                                if (telemetryData != null)
                                    EnqueueMessage(context, MessageType.TelemetryUpdate, telemetryData);
                            }
                            catch (Exception ex)
                            {
                                FileLogger.LogError(AppIdentifiers.IpcLogFile, $"[IPC Server] Failed to send initial snapshots to client {context.Id}", ex);
                            }
                        }
                        else if (packet.Type == MessageType.Heartbeat)
                        {
                            // Already updated LastHeartbeat above
                        }
                        else if (packet.Type == MessageType.CommandRequest)
                        {
                            try
                            {
                                var request = JsonSerializer.Deserialize<CommandRequest>(packet.Payload);
                                if (request != null)
                                {
                                    FileLogger.Log(AppIdentifiers.IpcLogFile, $"[IPC Server] Command received from client {context.Id}: {request.Command}");
                                    // ACK the command immediately
                                    EnqueueMessage(context, MessageType.Acknowledge, packet.SequenceId);
                                    _ = HandleCommandAsync(context, request);
                                }
                            }
                            catch (Exception ex)
                            {
                                FileLogger.LogError(AppIdentifiers.IpcLogFile, $"[IPC Server] Failed to process command from client {context.Id}", ex);
                            }
                        }
                    }
                    catch (JsonException ex)
                    {
                        FileLogger.LogError(AppIdentifiers.IpcLogFile, $"[IPC Server] JSON deserialization error from client {context.Id}", ex);
                    }
                    catch (Exception ex)
                    {
                        FileLogger.LogError(AppIdentifiers.IpcLogFile, $"[IPC Server] Packet processing error from client {context.Id}", ex);
                    }
                }
            }
            catch (IOException ex)
            {
                FileLogger.LogError(AppIdentifiers.IpcLogFile, $"[IPC Server] IO error with client {context.Id}", ex);
            }
            catch (OperationCanceledException)
            {
                FileLogger.Log(AppIdentifiers.IpcLogFile, $"[IPC Server] Client {context.Id} operation cancelled");
            }
            catch (Exception ex)
            {
                FileLogger.LogError(AppIdentifiers.IpcLogFile, $"[IPC Server] Unexpected error with client {context.Id}", ex);
            }
            finally
            {
                context.MessageQueue.CompleteAdding();
                _clients.TryRemove(context.Id, out _);
                FileLogger.Log(AppIdentifiers.IpcLogFile, $"[IPC Server] Client {context.Id} disconnected and cleaned up");
            }
        }

        private async Task ProcessOutgoingMessagesAsync(ClientContext context, CancellationToken token)
        {
            try
            {
                while (!token.IsCancellationRequested && !context.MessageQueue.IsCompleted)
                {
                    if (context.MessageQueue.TryTake(out string? message, 100, token))
                    {
                        if (!await TryWriteWithRetryAsync(context, message, maxRetries: 3).ConfigureAwait(false))
                        {
                            FileLogger.LogWarning(AppIdentifiers.IpcLogFile, $"[IPC Server] Failed to write message to client {context.Id} after retries. Disconnecting.");
                            try { context.Writer.Dispose(); } catch { }
                            break;
                        }
                    }
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                FileLogger.LogError(AppIdentifiers.IpcLogFile, $"[IPC Server] Error in message processing for client {context.Id}", ex);
            }
        }

        /// <summary>
        /// Attempts to write a message to the client with retry logic.
        /// </summary>
        private async Task<bool> TryWriteWithRetryAsync(ClientContext context, string message, int maxRetries = 3)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            bool succeeded = false;

            for (int attempt = 0; attempt < maxRetries; attempt++)
            {
                try
                {
                    await context.Writer.WriteLineAsync(message).ConfigureAwait(false);
                    await context.Writer.FlushAsync().ConfigureAwait(false);
                    succeeded = true;
                    break;
                }
                catch (IOException ex) when (attempt < maxRetries - 1)
                {
                    int delayMs = 100 * (int)Math.Pow(2, attempt);
                    FileLogger.LogWarning(AppIdentifiers.IpcLogFile, $"[IPC Server] Write attempt {attempt + 1} failed for client {context.Id}: {ex.Message}. Retrying in {delayMs}ms...");
                    await Task.Delay(delayMs).ConfigureAwait(false);
                }
                catch (ObjectDisposedException)
                {
                    FileLogger.LogWarning(AppIdentifiers.IpcLogFile, $"[IPC Server] Writer disposed for client {context.Id}. Cannot retry.");
                    break;
                }
                catch (Exception ex)
                {
                    FileLogger.LogError(AppIdentifiers.IpcLogFile, $"[IPC Server] Unexpected write error for client {context.Id}", ex);
                    break;
                }
            }

            sw.Stop();
            RansomGuard.Core.Services.PerformanceMonitor.Instance.RecordIpcWrite(sw.Elapsed.TotalMilliseconds, succeeded);

            if (!succeeded)
                FileLogger.LogError(AppIdentifiers.IpcLogFile, $"[IPC Server] All {maxRetries} write attempts failed for client {context.Id}");

            return succeeded;
        }

        /// <summary>
        /// Validates a command request for security and correctness.
        /// </summary>
        private bool ValidateCommandRequest(CommandRequest request)
        {
            // Check if command requires arguments
            bool requiresArguments = request.Command != CommandType.UpdatePaths && 
                                    request.Command != CommandType.ClearSafeFiles;
            
            if (requiresArguments && string.IsNullOrWhiteSpace(request.Arguments))
            {
                FileLogger.LogWarning(AppIdentifiers.IpcLogFile, $"[IPC Server] Command {request.Command} requires arguments but none provided");
                return false;
            }

            // Validate file path arguments
            if (request.Command == CommandType.QuarantineFile || 
                request.Command == CommandType.RestoreFile || 
                request.Command == CommandType.DeleteFile)
            {
                if (!ValidateFilePath(request.Arguments))
                {
                    FileLogger.LogWarning(AppIdentifiers.IpcLogFile, $"[IPC Server] Invalid file path for {request.Command}: {request.Arguments}");
                    return false;
                }
            }

            // Validate process ID for KillProcess
            if (request.Command == CommandType.KillProcess)
            {
                if (!int.TryParse(request.Arguments, out int pid) || pid <= 0)
                {
                    FileLogger.LogWarning(AppIdentifiers.IpcLogFile, $"[IPC Server] Invalid process ID: {request.Arguments}");
                    return false;
                }
            }

            // Validate process name for whitelist operations
            if (request.Command == CommandType.WhitelistProcess || 
                request.Command == CommandType.RemoveWhitelist)
            {
                if (string.IsNullOrWhiteSpace(request.Arguments) || 
                    request.Arguments.Length > 260 || // MAX_PATH
                    request.Arguments.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
                {
                    FileLogger.LogWarning(AppIdentifiers.IpcLogFile, $"[IPC Server] Invalid process name: {request.Arguments}");
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Validates a file path for security (prevents path traversal, validates format).
        /// </summary>
        private bool ValidateFilePath(string? path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return false;

            try
            {
                // Check if path is rooted (absolute)
                if (!Path.IsPathRooted(path))
                {
                    FileLogger.LogWarning(AppIdentifiers.IpcLogFile, $"[IPC Server] Path is not rooted: {path}");
                    return false;
                }

                // Get canonical path to prevent traversal attacks
                string fullPath = Path.GetFullPath(path);
                
                // Ensure the canonical path matches the input (prevents ../ attacks)
                if (!string.Equals(fullPath, path, StringComparison.OrdinalIgnoreCase))
                {
                    FileLogger.LogWarning(AppIdentifiers.IpcLogFile, $"[IPC Server] Path traversal detected: {path} -> {fullPath}");
                    return false;
                }

                // Check path length
                if (path.Length > 260) // MAX_PATH on Windows
                {
                    FileLogger.LogWarning(AppIdentifiers.IpcLogFile, $"[IPC Server] Path too long: {path.Length} characters");
                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                FileLogger.LogWarning(AppIdentifiers.IpcLogFile, $"[IPC Server] Path validation error: {ex.Message}");
                return false;
            }
        }



        private class MassEncryptionPayload
        {
            public string ThreatId { get; set; } = string.Empty;
            public bool ShouldMitigate { get; set; }
            public bool IsUserInitiated { get; set; }
            public int ProcessId { get; set; }
            public string ProcessName { get; set; } = string.Empty;
            public List<string> FilesToQuarantine { get; set; } = new();
        }

        private void ReliableBroadcast<T>(MessageType type, T data) => Broadcast(type, data, dropOldest: false);

        public void Broadcast<T>(MessageType type, T data, bool dropOldest)
        {
            var packet = new IpcPacket { Type = type, Payload = JsonSerializer.Serialize(data) };
            var json = JsonSerializer.Serialize(packet);

            // Snapshot values to avoid issues if a client disconnects mid-broadcast
            foreach (var ctx in _clients.Values.ToArray())
            {
                if (!ctx.IsHandshaked && type != MessageType.HandshakeResponse) continue;

                if (!ctx.MessageQueue.IsAddingCompleted)
                {
                    lock (ctx.MessageQueue)
                    {
                        if ((dropOldest || type == MessageType.ThreatDetected) && 
                            ctx.MessageQueue.Count >= AppConstants.Ipc.MessageQueueHighWaterMark)
                        {
                            ctx.MessageQueue.TryTake(out _);
                        }

                        if (!ctx.MessageQueue.TryAdd(json))
                        {
                            if (type == MessageType.ThreatDetected)
                            {
                                FileLogger.LogError(AppIdentifiers.IpcLogFile, $"[IPC Server] CRITICAL FAILURE: Could not enqueue THREAT ALERT to client {ctx.Id}.");
                            }
                            else
                            {
                                FileLogger.LogWarning(AppIdentifiers.IpcLogFile, $"[IPC Server] Failed to enqueue message of type {type} to client {ctx.Id}. Queue full.");
                            }
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

        /// <summary>
        /// Validates and dispatches a command from a connected client.
        /// All commands go through ValidateCommandRequest() before execution.
        /// </summary>
        private async Task HandleCommandAsync(ClientContext context, CommandRequest request)
        {
            // SECURITY: Validate all command requests before execution
            if (!ValidateCommandRequest(request))
            {
                FileLogger.LogWarning(AppIdentifiers.IpcLogFile, $"[IPC Server] Rejected invalid command from client {context.Id}: {request.Command}");
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
                            FileLogger.Log(AppIdentifiers.IpcLogFile, $"[IPC Server] Killed process PID: {pid}");
                        }
                        break;

                    case CommandType.QuarantineFile:
                        if (!string.IsNullOrEmpty(request.Arguments))
                        {
                            await _monitorService.QuarantineFile(request.Arguments).ConfigureAwait(false);
                            var updatedThreats = _monitorService.GetRecentThreats()
                                .Where(t => string.Equals(t.Path, request.Arguments, StringComparison.OrdinalIgnoreCase));
                            foreach (var t in updatedThreats) ReliableBroadcast(MessageType.ThreatDetected, t);
                            FileLogger.Log(AppIdentifiers.IpcLogFile, $"[IPC Server] Quarantined file: {request.Arguments}");
                        }
                        break;

                    case CommandType.UpdatePaths:
                        _monitorService.InitializeWatchers();
                        FileLogger.Log(AppIdentifiers.IpcLogFile, "[IPC Server] Updated monitored paths");
                        break;

                    case CommandType.RestoreFile:
                        if (!string.IsNullOrEmpty(request.Arguments))
                        {
                            await _monitorService.RestoreQuarantinedFile(request.Arguments).ConfigureAwait(false);
                            FileLogger.Log(AppIdentifiers.IpcLogFile, $"[IPC Server] Restored file: {request.Arguments}");
                        }
                        break;

                    case CommandType.DeleteFile:
                        if (!string.IsNullOrEmpty(request.Arguments))
                        {
                            await _monitorService.DeleteQuarantinedFile(request.Arguments).ConfigureAwait(false);
                            FileLogger.Log(AppIdentifiers.IpcLogFile, $"[IPC Server] Deleted quarantined file: {request.Arguments}");
                        }
                        break;

                    case CommandType.ClearSafeFiles:
                        await _monitorService.ClearSafeFiles().ConfigureAwait(false);
                        FileLogger.Log(AppIdentifiers.IpcLogFile, "[IPC Server] Cleared safe files");
                        break;

                    case CommandType.WhitelistProcess:
                        if (!string.IsNullOrEmpty(request.Arguments))
                        {
                            await _monitorService.WhitelistProcess(request.Arguments).ConfigureAwait(false);
                            FileLogger.Log(AppIdentifiers.IpcLogFile, $"[IPC Server] Whitelisted process: {request.Arguments}");
                        }
                        break;

                    case CommandType.RemoveWhitelist:
                        if (!string.IsNullOrEmpty(request.Arguments))
                        {
                            await _monitorService.RemoveWhitelist(request.Arguments).ConfigureAwait(false);
                            FileLogger.Log(AppIdentifiers.IpcLogFile, $"[IPC Server] Removed whitelist: {request.Arguments}");
                        }
                        break;

                    case CommandType.MitigateThreat:
                        if (!string.IsNullOrEmpty(request.Arguments))
                        {
                            await _monitorService.MitigateThreat(request.Arguments).ConfigureAwait(false);
                            FileLogger.Log(AppIdentifiers.IpcLogFile, $"[IPC Server] Mitigated threat ID: {request.Arguments}");
                        }
                        break;

                    case CommandType.HandleMassEncryption:
                        if (!string.IsNullOrEmpty(request.Arguments))
                        {
                            try
                            {
                                var payload = JsonSerializer.Deserialize<MassEncryptionPayload>(request.Arguments);
                                if (payload != null)
                                {
                                    await _monitorService.HandleMassEncryptionResponse(
                                        payload.ThreatId,
                                        payload.ShouldMitigate,
                                        payload.IsUserInitiated,
                                        payload.ProcessId,
                                        payload.ProcessName,
                                        payload.FilesToQuarantine).ConfigureAwait(false);
                                    FileLogger.Log(AppIdentifiers.IpcLogFile, $"[IPC Server] Handled mass encryption response for process: {payload.ProcessName} (PID: {payload.ProcessId})");
                                }
                            }
                            catch (Exception ex)
                            {
                                FileLogger.LogError(AppIdentifiers.IpcLogFile, "[IPC Server] Failed to deserialize mass encryption payload", ex);
                            }
                        }
                        break;

                    case CommandType.ClearHistory:
                        await _monitorService.ClearActivityHistory().ConfigureAwait(false);
                        FileLogger.Log(AppIdentifiers.IpcLogFile, "[IPC Server] Activity history cleared");
                        break;

                    default:
                        FileLogger.LogWarning(AppIdentifiers.IpcLogFile, $"[IPC Server] Unknown command type: {request.Command}");
                        break;
                }
            }
            catch (Exception ex)
            {
                FileLogger.LogError(AppIdentifiers.IpcLogFile, $"[IPC Server] Error executing command {request.Command} from client {context.Id}", ex);
            }
        }

        /// <summary>
        /// Reads a line from the stream with a maximum length to prevent DoS via infinite line buffering.
        /// </summary>
        private async Task<string?> ReadLimitedLineAsync(StreamReader reader, int maxLength, CancellationToken token)
        {
            var sb = new StringBuilder();
            var buffer = new char[1];
            
            while (sb.Length < maxLength)
            {
                int read = await reader.ReadAsync(buffer, 0, 1).ConfigureAwait(false);
                if (read == 0) return sb.Length > 0 ? sb.ToString() : null;
                
                char c = buffer[0];
                if (c == '\n') return sb.ToString().TrimEnd('\r');
                
                sb.Append(c);
            }

            FileLogger.LogWarning(AppIdentifiers.IpcLogFile, $"[IPC Server] Rejected line exceeding maximum length of {maxLength} characters.");
            // Drain the rest of the line to keep the stream synchronized? 
            // For IPC, it's safer to just disconnect the abusive client.
            throw new InvalidOperationException($"Line length limit ({maxLength}) exceeded.");
        }
    }
}
