using System;
using System.IO;
using System.IO.Pipes;
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
                catch { }
                await Task.Delay(2000, token);
            }
        }

        public void Stop()
        {
            _cts?.Cancel();
        }

        private async Task ListenLoop(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    using var pipeServer = new NamedPipeServerStream(PipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
                    await pipeServer.WaitForConnectionAsync(token);

                    await HandleClient(pipeServer, token);
                }
                catch (OperationCanceledException) { }
                catch (Exception ex)
                {
                    Console.WriteLine($"Pipe Server Error: {ex.Message}");
                    await Task.Delay(1000, token);
                }
            }
        }

        private async Task HandleClient(NamedPipeServerStream pipe, CancellationToken token)
        {
            using var reader = new StreamReader(pipe);
            using var writer = new StreamWriter(pipe) { AutoFlush = true };

            // Send initial state
            foreach (var activity in _monitorService.GetRecentFileActivities())
                await SendMessage(writer, MessageType.FileActivity, activity);

            foreach (var threat in _monitorService.GetRecentThreats())
                await SendMessage(writer, MessageType.ThreatDetected, threat);

            // Command Listener Loop
            while (pipe.IsConnected && !token.IsCancellationRequested)
            {
                var line = await reader.ReadLineAsync();
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
                        try { System.Diagnostics.Process.GetProcessById(pid).Kill(); } catch { }
                    }
                    break;
            }
        }

        private void Broadcast<T>(MessageType type, T data)
        {
            // Simple broadcast for now (to the next connected client)
            // In a better implementation, we'd manage a list of clients.
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
