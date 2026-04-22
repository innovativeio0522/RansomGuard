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
            public BlockingCollection<string> MessageQueue { get; } = new(100); // Smaller queue for responsiveness
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
                    // Telemetry is lossy — we use DropOldest strategy
                    Broadcast(MessageType.TelemetryUpdate, telemetry, dropOldest: true);
                }
                catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"TelemetryBroadcastLoop error: {ex.Message}"); }
                await Task.Delay(2000, token).ConfigureAwait(false);
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
                await Task.Delay(5000, token).ConfigureAwait(false); // Slowed down slightly for stability
            }
        }

        private async Task HeartbeatMonitorLoop(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                var now = DateTime.Now;
                foreach (var client in _clients.Values)
                {
                    if ((now - client.LastHeartbeat).TotalSeconds > 30)
                    {
                        System.Diagnostics.Debug.WriteLine($"[IPC] Client {client.Id} timed out. Removing.");
                        client.MessageQueue.CompleteAdding();
                        _clients.TryRemove(client.Id, out _);
                    }
                }
                await Task.Delay(10000, token).ConfigureAwait(false);
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
                try
                {
                    var pipeSecurity = new PipeSecurity();
                    var currentUser = WindowsIdentity.GetCurrent().User;
                    if (currentUser != null)
                        pipeSecurity.AddAccessRule(new PipeAccessRule(currentUser, PipeAccessRights.FullControl | PipeAccessRights.CreateNewInstance, AccessControlType.Allow));

                    pipeSecurity.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.AuthenticatedUserSid, null), PipeAccessRights.ReadWrite | PipeAccessRights.CreateNewInstance, AccessControlType.Allow));
                    pipeSecurity.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null), PipeAccessRights.FullControl, AccessControlType.Allow));

                    Console.WriteLine($"[IPC Server] Creating pipe: {_pipeName}");
                    pipeServer = NamedPipeServerStreamAcl.Create(_pipeName, PipeDirection.InOut, NamedPipeServerStream.MaxAllowedServerInstances, PipeTransmissionMode.Byte, PipeOptions.Asynchronous, 0, 0, pipeSecurity);

                    Console.WriteLine("[IPC Server] Waiting for connection...");
                    await pipeServer.WaitForConnectionAsync(token).ConfigureAwait(false);
                    Console.WriteLine("[IPC Server] Client connected!");
                    
                    var clientPipe = pipeServer;
                    pipeServer = null; 
                    _ = Task.Run(async () => {
                        try { await HandleClient(clientPipe, token).ConfigureAwait(false); }
                        catch (Exception ex) { Console.WriteLine($"[IPC Server] HandleClient error: {ex.Message}"); }
                        finally { clientPipe.Dispose(); Console.WriteLine("[IPC Server] Client disconnected."); }
                    }, token);
                }
                catch (Exception ex) 
                { 
                    pipeServer?.Dispose(); 
                    if (!token.IsCancellationRequested) await Task.Delay(2000, token).ConfigureAwait(false); 
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

            try
            {
                while (pipe.IsConnected && !token.IsCancellationRequested)
                {
                    var line = await reader.ReadLineAsync(token).ConfigureAwait(false);
                    if (line == null) break;

                    try
                    {
                        var packet = JsonSerializer.Deserialize<IpcPacket>(line);
                        if (packet == null) continue;

                        context.LastHeartbeat = DateTime.Now;

                        if (packet.Type == MessageType.HandshakeRequest)
                        {
                            Console.WriteLine("[IPC Server] Handshake received from client.");
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
                    catch { }
                }
            }
            finally
            {
                context.MessageQueue.CompleteAdding();
                _clients.TryRemove(context.Id, out _);
            }
        }

        private void ProcessOutgoingMessages(ClientContext context, CancellationToken token)
        {
            try
            {
                foreach (var message in context.MessageQueue.GetConsumingEnumerable(token))
                {
                    context.Writer.WriteLine(message);
                }
            }
            catch { }
        }

        private async Task HandleCommand(CommandRequest? request, StreamWriter writer)
        {
            if (request == null) return;
            switch (request.Command)
            {

                case CommandType.KillProcess:
                    if (int.TryParse(request.Arguments, out int pid)) await _monitorService.KillProcess(pid).ConfigureAwait(false);
                    break;
                case CommandType.QuarantineFile:
                    if (!string.IsNullOrEmpty(request.Arguments))
                    {
                        await _monitorService.QuarantineFile(request.Arguments).ConfigureAwait(false);
                        var updatedThreats = _monitorService.GetRecentThreats().Where(t => string.Equals(t.Path, request.Arguments, StringComparison.OrdinalIgnoreCase));
                        foreach (var t in updatedThreats) ReliableBroadcast(MessageType.ThreatDetected, t);
                    }
                    break;
                case CommandType.UpdatePaths: _monitorService.InitializeWatchers(); break;
                case CommandType.RestoreFile: if (!string.IsNullOrEmpty(request.Arguments)) await _monitorService.RestoreQuarantinedFile(request.Arguments).ConfigureAwait(false); break;
                case CommandType.DeleteFile: if (!string.IsNullOrEmpty(request.Arguments)) await _monitorService.DeleteQuarantinedFile(request.Arguments).ConfigureAwait(false); break;
                case CommandType.ClearSafeFiles: await _monitorService.ClearSafeFiles().ConfigureAwait(false); break;
                case CommandType.WhitelistProcess: if (!string.IsNullOrEmpty(request.Arguments)) await _monitorService.WhitelistProcess(request.Arguments).ConfigureAwait(false); break;
                case CommandType.RemoveWhitelist: if (!string.IsNullOrEmpty(request.Arguments)) await _monitorService.RemoveWhitelist(request.Arguments).ConfigureAwait(false); break;
            }
        }

        private void ReliableBroadcast<T>(MessageType type, T data) => Broadcast(type, data, dropOldest: false);

        private void Broadcast<T>(MessageType type, T data, bool dropOldest)
        {
            var packet = new IpcPacket { Type = type, Payload = JsonSerializer.Serialize(data) };
            var json = JsonSerializer.Serialize(packet);

            foreach (var ctx in _clients.Values)
            {
                if (!ctx.IsHandshaked && type != MessageType.HandshakeResponse) continue;

                if (!ctx.MessageQueue.IsAddingCompleted)
                {
                    if (dropOldest && ctx.MessageQueue.Count >= 90)
                    {
                        // Drop oldest to avoid blocking the broadcast loop
                        ctx.MessageQueue.TryTake(out _);
                    }
                    ctx.MessageQueue.TryAdd(json);
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
