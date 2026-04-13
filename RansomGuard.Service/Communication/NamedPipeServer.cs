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

namespace RansomGuard.Service.Communication
{
    public class NamedPipeServer
    {
        private const string PipeName = "RansomGuardPipe";
        private readonly ISystemMonitorService _monitorService;
        private CancellationTokenSource? _cts;

        // Track all active connected client writers for live broadcasting
        private readonly ConcurrentDictionary<Guid, StreamWriter> _clients = new();

        public NamedPipeServer(ISystemMonitorService monitorService)
        {
            _monitorService = monitorService;
        }

        public void Start()
        {
            _cts = new CancellationTokenSource();
            Task.Run(() => ListenLoop(_cts.Token));
            Task.Run(() => TelemetryBroadcastLoop(_cts.Token));

            _monitorService.FileActivityDetected += (activity) => Broadcast(MessageType.FileActivity, activity);
            _monitorService.ThreatDetected += (threat) => Broadcast(MessageType.ThreatDetected, threat);
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
                await Task.Delay(2000, token);
            }
        }

        public void Stop()
        {
            _cts?.Cancel();
            
            // Dispose all active client connections
            foreach (var (id, writer) in _clients)
            {
                try
                {
                    writer.Dispose();
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
                    // Allow any local user (including non-elevated) to connect to the service pipe
                    var pipeSecurity = new PipeSecurity();
                    pipeSecurity.AddAccessRule(new PipeAccessRule(
                        new SecurityIdentifier(WellKnownSidType.AuthenticatedUserSid, null),
                        PipeAccessRights.ReadWrite | PipeAccessRights.CreateNewInstance,
                        AccessControlType.Allow));
                    pipeSecurity.AddAccessRule(new PipeAccessRule(
                        new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null),
                        PipeAccessRights.FullControl,
                        AccessControlType.Allow));

                    pipeServer = NamedPipeServerStreamAcl.Create(
                        PipeName, PipeDirection.InOut, 1,
                        PipeTransmissionMode.Byte, PipeOptions.Asynchronous,
                        0, 0, pipeSecurity);

                    // Add timeout to prevent indefinite hanging
                    using var timeoutCts = new CancellationTokenSource(TimeSpan.FromMinutes(5));
                    using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(token, timeoutCts.Token);
                    
                    await pipeServer.WaitForConnectionAsync(linkedCts.Token);
                    
                    // Fire HandleClient on a separate task without awaiting, so ListenLoop immediately creates a new server instance
                    var clientPipe = pipeServer;
                    pipeServer = null; // Transfer ownership to HandleClient task
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            await HandleClient(clientPipe, token);
                        }
                        finally
                        {
                            clientPipe.Dispose();
                        }
                    }, token);
                }
                catch (OperationCanceledException) { }
                catch (Exception ex)
                {
                    Console.WriteLine($"Pipe Server Error: {ex.Message}");
                    pipeServer?.Dispose();
                    if (!token.IsCancellationRequested)
                        await Task.Delay(1000, token);
                }
            }
        }

        private async Task HandleClient(NamedPipeServerStream pipe, CancellationToken token)
        {
            using var reader = new StreamReader(pipe);
            using var writer = new StreamWriter(pipe) { AutoFlush = true };

            var clientId = Guid.NewGuid();
            _clients[clientId] = writer;

            try
            {
                // Send initial state snapshot
                foreach (var activity in _monitorService.GetRecentFileActivities())
                    await SendMessage(writer, MessageType.FileActivity, activity);

                foreach (var threat in _monitorService.GetRecentThreats())
                    await SendMessage(writer, MessageType.ThreatDetected, threat);

                // Send initial telemetry immediately on connect
                await SendMessage(writer, MessageType.TelemetryUpdate, _monitorService.GetTelemetry());

                // Command Listener Loop
                while (pipe.IsConnected && !token.IsCancellationRequested)
                {
                    var line = await reader.ReadLineAsync(token);
                    if (line == null) break;

                    try
                    {
                        var packet = JsonSerializer.Deserialize<IpcPacket>(line);
                        if (packet?.Type == MessageType.CommandRequest)
                        {
                            var request = JsonSerializer.Deserialize<CommandRequest>(packet.Payload);
                            await HandleCommand(request, writer);
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
                _clients.TryRemove(clientId, out _);
            }
        }

        private async Task HandleCommand(CommandRequest? request, StreamWriter writer)
        {
            if (request == null) return;

            switch (request.Command)
            {
                case CommandType.PerformScan:
                    await _monitorService.PerformQuickScan();
                    break;
                case CommandType.KillProcess:
                    if (int.TryParse(request.Arguments, out int pid))
                    {
                        try
                        {
                            System.Diagnostics.Process.GetProcessById(pid).Kill();
                        }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Debug.WriteLine($"HandleCommand KillProcess error: {ex.Message}");
                        }
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

            var disconnectedClients = new List<Guid>();
            
            foreach (var (id, writer) in _clients)
            {
                try
                {
                    writer.WriteLine(json);
                }
                catch
                {
                    // Client disconnected — mark for removal and disposal
                    disconnectedClients.Add(id);
                }
            }
            
            // Remove and dispose disconnected clients
            foreach (var id in disconnectedClients)
            {
                if (_clients.TryRemove(id, out var writer))
                {
                    try
                    {
                        writer.Dispose();
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Broadcast client disposal error: {ex.Message}");
                    }
                }
            }
        }

        private async Task SendMessage<T>(StreamWriter writer, MessageType type, T data)
        {
            var packet = new IpcPacket
            {
                Type = type,
                Payload = JsonSerializer.Serialize(data)
            };
            await writer.WriteLineAsync(JsonSerializer.Serialize(packet));
        }
    }
}
