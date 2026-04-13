using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using RansomGuard.Core.Interfaces;
using RansomGuard.Core.Models;
using RansomGuard.Core.IPC;

namespace RansomGuard.Services
{
    public class ServicePipeClient : ISystemMonitorService
    {
        private const string PipeName = "RansomGuardPipe";
        private NamedPipeClientStream? _pipeClient;
        private StreamWriter? _writer;
        private CancellationTokenSource? _cts;

        public event Action<FileActivity>? FileActivityDetected;
        public event Action<Threat>? ThreatDetected;
        public event Action<bool>? ConnectionStatusChanged;

        public bool IsConnected { get; private set; }

        private readonly List<FileActivity> _recentActivities = new();
        private readonly List<Threat> _recentThreats = new();
        private TelemetryData _lastTelemetry = new();

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
            while (!token.IsCancellationRequested)
            {
                try
                {
                    _pipeClient = new NamedPipeClientStream(".", PipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
                    await _pipeClient.ConnectAsync(5000, token);
                    
                    IsConnected = true;
                    ConnectionStatusChanged?.Invoke(true);

                    _writer = new StreamWriter(_pipeClient) { AutoFlush = true };
                    using var reader = new StreamReader(_pipeClient);

                    while (_pipeClient.IsConnected && !token.IsCancellationRequested)
                    {
                        var line = await reader.ReadLineAsync();
                        if (line == null) break;

                        HandlePacket(line);
                    }
                }
                catch (Exception)
                {
                    if (IsConnected)
                    {
                        IsConnected = false;
                        ConnectionStatusChanged?.Invoke(false);
                    }
                    await Task.Delay(2000, token);
                }
                finally
                {
                    if (IsConnected)
                    {
                        IsConnected = false;
                        ConnectionStatusChanged?.Invoke(false);
                    }
                }
            }
        }

        private void HandlePacket(string json)
        {
            try
            {
                var packet = JsonSerializer.Deserialize<IpcPacket>(json);
                if (packet == null) return;

                switch (packet.Type)
                {
                    case MessageType.FileActivity:
                        var activity = JsonSerializer.Deserialize<FileActivity>(packet.Payload);
                        if (activity != null)
                        {
                            _recentActivities.Insert(0, activity);
                            FileActivityDetected?.Invoke(activity);
                        }
                        break;

                    case MessageType.ThreatDetected:
                        var threat = JsonSerializer.Deserialize<Threat>(packet.Payload);
                        if (threat != null)
                        {
                            _recentThreats.Insert(0, threat);
                            ThreatDetected?.Invoke(threat);
                        }
                        break;

                    case MessageType.TelemetryUpdate:
                        var tele = JsonSerializer.Deserialize<TelemetryData>(packet.Payload);
                        if (tele != null) _lastTelemetry = tele;
                        break;
                }
            }
            catch { }
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

        public IEnumerable<Threat> GetRecentThreats() => _recentThreats;
        public IEnumerable<FileActivity> GetRecentFileActivities() => _recentActivities;
        public IEnumerable<ProcessInfo> GetActiveProcesses() => Enumerable.Empty<ProcessInfo>(); // Service handles this
        
        public DateTime GetLastScanTime() => DateTime.Now;
        public async Task PerformQuickScan() => await SendCommand(CommandType.PerformScan);

        public double GetSystemCpuUsage() => _lastTelemetry.CpuUsage;
        public long GetSystemMemoryUsage() => _lastTelemetry.MemoryUsage;
        public int GetMonitoredFilesCount() => _lastTelemetry.MonitoredFilesCount;
        public TelemetryData GetTelemetry() => _lastTelemetry;

        public IEnumerable<string> GetQuarantinedFiles() => Enumerable.Empty<string>(); // Client doesn't need the list, just the count from telemetry
        public double GetQuarantineStorageUsage() => _lastTelemetry.QuarantineStorageMb;
        public async Task KillProcess(int pid) => await SendCommand(CommandType.KillProcess, pid.ToString());
    }
}
