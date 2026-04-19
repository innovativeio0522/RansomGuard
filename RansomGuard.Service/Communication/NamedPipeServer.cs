using System;
using System.Collections.Concurrent;
using System.IO;
using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using RansomGuard.Core.IPC;
using RansomGuard.Core.Interfaces;
using RansomGuard.Core.Models;
using RansomGuard.Service.Engine;

namespace RansomGuard.Service.Communication
{
    public class NamedPipeServer
    {
        private const string PipeName = "SentinelGuardPipe";
        private readonly ISystemMonitorService _monitorService;
        private CancellationTokenSource? _cts;

        private class ClientContext
        {
            public StreamWriter Writer { get; }
            public BlockingCollection<string> MessageQueue { get; } = new(1000);
            public Task ProcessorTask { get; set; } = Task.CompletedTask;

            public ClientContext(StreamWriter writer)
            {
                Writer = writer;
            }
        }

        // Track all active connected client contexts for live broadcasting
        private readonly ConcurrentDictionary<Guid, ClientContext> _clients = new();

        public NamedPipeServer(ISystemMonitorService monitorService)
        {
            _monitorService = monitorService;
        }

        public void Start()
        {
            _cts = new CancellationTokenSource();
            Task.Run(() => ListenLoop(_cts.Token));
            Task.Run(() => TelemetryBroadcastLoop(_cts.Token));
            Task.Run(() => ProcessListBroadcastLoop(_cts.Token));

            _monitorService.FileActivityDetected += (activity) => Broadcast(MessageType.FileActivity, activity);
            _monitorService.ThreatDetected += (threat) => Broadcast(MessageType.ThreatDetected, threat);
            _monitorService.ScanCompleted += (summary) => Broadcast(MessageType.ScanCompleted, summary);
        }

        private async Task TelemetryBroadcastLoop(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    var telemetry = _monitorService.GetTelemetry();
                    Broadcast(MessageType.TelemetryUpdate, telemetry);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"TelemetryBroadcastLoop error: {ex.Message}");
                }
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
                    Broadcast(MessageType.ProcessListUpdate, processes);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"ProcessListBroadcastLoop error: {ex.Message}");
                }
                // Broadcast every 3 seconds as recommended
                await Task.Delay(3000, token).ConfigureAwait(false);
            }
        }

        public void Stop()
        {
            _cts?.Cancel();
            
            // Dispose all active client connections
            foreach (var (id, ctx) in _clients)
            {
                try
                {
                    ctx.MessageQueue.CompleteAdding();
                    ctx.Writer.Dispose();
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Stop client disposal error: {ex.Message}");
                }
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
                    
                    // Allow the current user (process owner) full control
                    var currentUser = WindowsIdentity.GetCurrent().User;
                    if (currentUser != null)
                    {
                        pipeSecurity.AddAccessRule(new PipeAccessRule(
                            currentUser,
                            PipeAccessRights.FullControl | PipeAccessRights.CreateNewInstance,
                            AccessControlType.Allow));
                    }

                    // Allow all authenticated users to connect
                    pipeSecurity.AddAccessRule(new PipeAccessRule(
                        new SecurityIdentifier(WellKnownSidType.AuthenticatedUserSid, null),
                        PipeAccessRights.ReadWrite | PipeAccessRights.CreateNewInstance,
                        AccessControlType.Allow));

                    // Required for service connectivity
                    pipeSecurity.AddAccessRule(new PipeAccessRule(
                        new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null),
                        PipeAccessRights.FullControl,
                        AccessControlType.Allow));

                    pipeServer = NamedPipeServerStreamAcl.Create(
                        PipeName, PipeDirection.InOut, NamedPipeServerStream.MaxAllowedServerInstances,
                        PipeTransmissionMode.Byte, PipeOptions.Asynchronous,
                        0, 0, pipeSecurity);

                    using var timeoutCts = new CancellationTokenSource(TimeSpan.FromMinutes(5));
                    using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(token, timeoutCts.Token);
                    
                    await pipeServer.WaitForConnectionAsync(linkedCts.Token).ConfigureAwait(false);
                    
                    var clientPipe = pipeServer;
                    pipeServer = null; 
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            await HandleClient(clientPipe, token).ConfigureAwait(false);
                        }
                        finally
                        {
                            clientPipe.Dispose();
                        }
                    }, token);
                }
                catch (OperationCanceledException) { }
                catch (UnauthorizedAccessException)
                {
                    Console.WriteLine("CRITICAL PIPE ERROR: Access Denied.");
                    Console.WriteLine(">>> RANSOMGUARD REQUIRES ADMINISTRATIVE PRIVILEGES TO START THE COMMUNICATION ENGINE.");
                    Console.WriteLine(">>> Please restart this service with 'Run as Administrator'.");
                    
                    pipeServer?.Dispose();
                    if (!token.IsCancellationRequested)
                        await Task.Delay(5000, token).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Pipe Server Error: {ex.Message}");
                    pipeServer?.Dispose();
                    if (!token.IsCancellationRequested)
                        await Task.Delay(2000, token).ConfigureAwait(false);
                }
            }
        }

        private async Task HandleClient(NamedPipeServerStream pipe, CancellationToken token)
        {
            using var reader = new StreamReader(pipe);
            using var writer = new StreamWriter(pipe) { AutoFlush = true };

            var clientId = Guid.NewGuid();
            var context = new ClientContext(writer);
            
            // Start the writer task for this specific client
            context.ProcessorTask = Task.Run(() => ProcessOutgoingMessages(context, token));
            
            _clients[clientId] = context;

            try
            {
                // Send initial state snapshot with specific snapshot types so the UI knows not to fire "Live" alerts
                foreach (var activity in _monitorService.GetRecentFileActivities())
                    EnqueueMessage(context, MessageType.FileActivitySnapshot, activity);

                foreach (var threat in _monitorService.GetRecentThreats())
                    EnqueueMessage(context, MessageType.ThreatDetectedSnapshot, threat);

                // Send initial telemetry immediately on connect
                EnqueueMessage(context, MessageType.TelemetryUpdate, _monitorService.GetTelemetry());
                
                // Send initial process list snapshot immediately on connect
                EnqueueMessage(context, MessageType.ProcessListUpdate, _monitorService.GetActiveProcesses());

                // Command Listener Loop (Inbound)
                while (pipe.IsConnected && !token.IsCancellationRequested)
                {
                    var line = await reader.ReadLineAsync(token).ConfigureAwait(false);
                    if (line == null) break;

                    try
                    {
                        var packet = JsonSerializer.Deserialize<IpcPacket>(line);
                        if (packet?.Type == MessageType.CommandRequest)
                        {
                            var request = JsonSerializer.Deserialize<CommandRequest>(packet.Payload);
                            await HandleCommand(request, writer).ConfigureAwait(false);
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Failed to process command: {ex.Message}");
                    }
                }
            }
            finally
            {
                context.MessageQueue.CompleteAdding();
                _clients.TryRemove(clientId, out _);
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
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"ProcessOutgoingMessages error for client: {ex.Message}");
            }
        }

        private async Task HandleCommand(CommandRequest? request, StreamWriter writer)
        {
            if (request == null) return;

            switch (request.Command)
            {
                case CommandType.PerformScan:
                    await _monitorService.PerformQuickScan().ConfigureAwait(false);
                    break;
                case CommandType.KillProcess:
                    if (int.TryParse(request.Arguments, out int pid))
                    {
                        await _monitorService.KillProcess(pid).ConfigureAwait(false);
                    }
                    break;
                case CommandType.ToggleShield:
                    // Reserved for future shield toggling logic
                    break;
                case CommandType.QuarantineFile:
                    if (!string.IsNullOrEmpty(request.Arguments))
                    {
                        await _monitorService.QuarantineFile(request.Arguments).ConfigureAwait(false);
                        // Broadcast the updated threat status to ALL connected clients so every
                        // page (Dashboard, Threat Alerts) removes the entry without needing a timer.
                        var updatedThreats = _monitorService.GetRecentThreats()
                            .Where(t => string.Equals(t.Path, request.Arguments, StringComparison.OrdinalIgnoreCase));
                        foreach (var t in updatedThreats)
                            Broadcast(MessageType.ThreatDetected, t);
                    }
                    break;
                case CommandType.UpdatePaths:
                    _monitorService.InitializeWatchers();
                    break;
                case CommandType.RestoreFile:
                    if (!string.IsNullOrEmpty(request.Arguments))
                    {
                        await _monitorService.RestoreQuarantinedFile(request.Arguments).ConfigureAwait(false);
                    }
                    break;
                case CommandType.DeleteFile:
                    if (!string.IsNullOrEmpty(request.Arguments))
                    {
                        await _monitorService.DeleteQuarantinedFile(request.Arguments).ConfigureAwait(false);
                    }
                    break;
                case CommandType.ClearSafeFiles:
                    await _monitorService.ClearSafeFiles().ConfigureAwait(false);
                    break;
                case CommandType.GetProcessList:
                    var processes = _monitorService.GetActiveProcesses();
                    Broadcast(MessageType.ProcessListUpdate, processes);
                    break;
                case CommandType.WhitelistProcess:
                    if (!string.IsNullOrEmpty(request.Arguments))
                    {
                        await _monitorService.WhitelistProcess(request.Arguments).ConfigureAwait(false);
                    }
                    break;
                case CommandType.RemoveWhitelist:
                    if (!string.IsNullOrEmpty(request.Arguments))
                    {
                        await _monitorService.RemoveWhitelist(request.Arguments).ConfigureAwait(false);
                    }
                    break;
            }
        }

        private void Broadcast<T>(MessageType type, T data)
        {
            var packet = new IpcPacket
            {
                Type = type,
                Payload = JsonSerializer.Serialize(data)
            };
            var json = JsonSerializer.Serialize(packet);

            foreach (var ctx in _clients.Values)
            {
                if (!ctx.MessageQueue.IsAddingCompleted)
                {
                    ctx.MessageQueue.TryAdd(json);
                }
            }
        }

        private void EnqueueMessage<T>(ClientContext context, MessageType type, T data)
        {
            var packet = new IpcPacket
            {
                Type = type,
                Payload = JsonSerializer.Serialize(data)
            };
            context.MessageQueue.TryAdd(JsonSerializer.Serialize(packet));
        }

        private async Task SendMessage<T>(StreamWriter writer, MessageType type, T data)
        {
            var packet = new IpcPacket
            {
                Type = type,
                Payload = JsonSerializer.Serialize(data)
            };
            await writer.WriteLineAsync(JsonSerializer.Serialize(packet)).ConfigureAwait(false);
        }
    }

}
