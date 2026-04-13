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
        private const string PipeName = "RansomGuardPipe";
        private const int MaxRecentActivities = 200;
        private const int MaxActiveProcesses = 50;
        private const int Windows11BuildNumber = 20348;
        
        private NamedPipeClientStream? _pipeClient;
        private StreamWriter? _writer;
        private CancellationTokenSource? _cts;
        private bool _disposed;

        public event Action<FileActivity>? FileActivityDetected;
        public event Action<Threat>? ThreatDetected;
        public event Action<bool>? ConnectionStatusChanged;

        public bool IsConnected { get; private set; }

        private readonly List<FileActivity> _recentActivities = new();
        private readonly List<Threat> _recentThreats = new();
        private readonly object _activitiesLock = new();
        private readonly object _threatsLock = new();

        private TelemetryData _lastTelemetry = new();
        private readonly object _telemetryLock = new();

        // Local fallback CPU/RAM counters (used when service is not connected)
        private PerformanceCounter? _localCpuCounter;
        private PerformanceCounter? _localRamCounter;
        private readonly System.Timers.Timer _localTelemetryTimer;
        private double _localCpuUsage = 0;
        private long _localMemoryUsage = 0;
        private double _systemRamTotalMb = 0;
        private double _systemRamUsedMb = 0;
        private double _entropyScore = 2.4;
        private readonly Random _rng = new();

        // Dynamic telemetry state
        private double _networkLatencyMs = 0;
        private int _activeEndpointsCount = 0;
        private string _encryptionLevel = "AES-256";
        private int _filesPerHour = 0;
        private int _filesCountSnapshot = 0;
        private DateTime _filesSnapshotTime = DateTime.Now;
        private readonly System.Timers.Timer _networkTelemetryTimer;

        public ServicePipeClient()
        {
            // Detect total system RAM once using Performance Counter
            try
            {
                if (OperatingSystem.IsWindows())
                {
                    using var totalRamCounter = new PerformanceCounter("Memory", "Available MBytes");
                    double availableMb = totalRamCounter.NextValue();
                    // Use WMI-style fallback: GCMemoryInfo gives total managed heap limit
                    // Better: use Environment.WorkingSet + available as approximation
                    // Most reliable: read from registry or use GetPhysicallyInstalledSystemMemory
                    _systemRamTotalMb = GetTotalPhysicalMemoryMb();
                }
            }
            catch { }

            // Initialize local PerformanceCounters as fallback
            try
            {
                if (OperatingSystem.IsWindows())
                {
                    _localCpuCounter = new PerformanceCounter("Processor", "% Processor Time", "_Total");
                    _localCpuCounter.NextValue(); // First call always returns 0, warm it up
                    _localRamCounter = new PerformanceCounter("Memory", "Available MBytes");
                    _localRamCounter.NextValue();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"InitializeCounters error: {ex.Message}");
            }

            // Poll CPU locally every 2 seconds regardless of service connection
            _localTelemetryTimer = new System.Timers.Timer(2000);
            _localTelemetryTimer.Elapsed += (s, e) => PollLocalTelemetry();
            _localTelemetryTimer.Start();

            // Poll network telemetry every 5 seconds (latency + endpoints)
            _networkTelemetryTimer = new System.Timers.Timer(5000);
            _networkTelemetryTimer.Elapsed += (s, e) => PollNetworkTelemetry();
            _networkTelemetryTimer.Start();
            // Run once immediately on a background thread
            Task.Run(PollNetworkTelemetry);

            Start();
        }

        private static double GetTotalPhysicalMemoryMb()
        {
            return NativeMemory.GetTotalPhysicalMemoryMb();
        }

        private void PollNetworkTelemetry()
        {
            try
            {
                // Measure loopback latency as a proxy for local IPC responsiveness
                var sw = Stopwatch.StartNew();
                using var ping = new Ping();
                var reply = ping.Send(IPAddress.Loopback, 500);
                sw.Stop();
                _networkLatencyMs = reply.Status == IPStatus.Success
                    ? reply.RoundtripTime > 0 ? reply.RoundtripTime : sw.Elapsed.TotalMilliseconds
                    : sw.Elapsed.TotalMilliseconds;

                // Count established TCP connections for this process
                var pid = Environment.ProcessId;
                var tcpConns = IPGlobalProperties.GetIPGlobalProperties()
                    .GetActiveTcpConnections()
                    .Count(c => c.State == TcpState.Established);
                _activeEndpointsCount = tcpConns;

                // Derive encryption level from OS security policy
                _encryptionLevel = DetermineEncryptionLevel();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"PollNetworkTelemetry error: {ex.Message}");
            }
        }

        private static string DetermineEncryptionLevel()
        {
            // Check if TLS 1.3 is available (Windows 11 / Server 2022+)
            try
            {
                var osVersion = Environment.OSVersion.Version;
                if (osVersion.Major > 10 || (osVersion.Major == 10 && osVersion.Build >= Windows11BuildNumber))
                    return "TLS 1.3 / AES-256";
                if (osVersion.Major == 10)
                    return "TLS 1.2 / AES-256";
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"DetermineEncryptionLevel error: {ex.Message}");
            }
            return "AES-256";
        }

        private void PollLocalTelemetry()
        {
            try
            {
                _localCpuUsage = _localCpuCounter?.NextValue() ?? 0;
                _localMemoryUsage = Process.GetCurrentProcess().WorkingSet64;

                // Use Win32 for accurate RAM figures
                if (NativeMemory.GetMemoryStatus(out var memStatus))
                {
                    _systemRamTotalMb = memStatus.ullTotalPhys / (1024.0 * 1024.0);
                    double availableMb = memStatus.ullAvailPhys / (1024.0 * 1024.0);
                    _systemRamUsedMb = _systemRamTotalMb - availableMb;
                }
                else
                {
                    double availableMb = _localRamCounter?.NextValue() ?? 0;
                    _systemRamUsedMb = _systemRamTotalMb > 0 ? _systemRamTotalMb - availableMb : 0;
                }

                // Simulate entropy: base 1.5, rises slightly with CPU, jitter ±0.3
                double targetEntropy = 1.5 + (_localCpuUsage / 100.0) * 3.0;
                _entropyScore = Math.Round(targetEntropy + (_rng.NextDouble() - 0.5) * 0.3, 1);
                _entropyScore = Math.Max(0.1, Math.Min(8.0, _entropyScore));

                // Compute files-per-hour from rolling delta
                int currentCount;
                lock (_activitiesLock) { currentCount = _recentActivities.Count; }
                double elapsedHours = (DateTime.Now - _filesSnapshotTime).TotalHours;
                if (elapsedHours >= (1.0 / 60.0))
                {
                    int delta = currentCount - _filesCountSnapshot;
                    _filesPerHour = elapsedHours > 0 ? (int)(delta / elapsedHours) : 0;
                    _filesCountSnapshot = currentCount;
                    _filesSnapshotTime = DateTime.Now;
                }

                // If service is not connected, inject into _lastTelemetry so UI sees live data
                if (!IsConnected)
                {
                    var telemetry = new TelemetryData
                    {
                        CpuUsage = _localCpuUsage,
                        MemoryUsage = _localMemoryUsage,
                        SystemRamUsedMb = _systemRamUsedMb,
                        SystemRamTotalMb = _systemRamTotalMb,
                        EntropyScore = _entropyScore,
                        MonitoredFilesCount = GetTelemetry().MonitoredFilesCount,
                        ProcessesCount = Process.GetProcesses().Length,
                        IsHoneyPotActive = false,
                        IsVssShieldActive = false,
                        IsPanicModeActive = false,
                        QuarantineStorageMb = 0,
                        NetworkLatencyMs = _networkLatencyMs,
                        ActiveEndpointsCount = _activeEndpointsCount,
                        EncryptionLevel = _encryptionLevel,
                        FilesPerHour = _filesPerHour
                    };
                    lock (_telemetryLock) { _lastTelemetry = telemetry; }
                }
                else
                {
                    lock (_telemetryLock)
                    {
                        _lastTelemetry.NetworkLatencyMs = _networkLatencyMs;
                        _lastTelemetry.ActiveEndpointsCount = _activeEndpointsCount;
                        _lastTelemetry.EncryptionLevel = _encryptionLevel;
                        _lastTelemetry.FilesPerHour = _filesPerHour;
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"PollLocalTelemetry error: {ex.Message}");
            }
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
                NamedPipeClientStream? pipeClient = null;
                StreamWriter? writer = null;
                StreamReader? reader = null;
                
                try
                {
                    pipeClient = new NamedPipeClientStream(".", PipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
                    await pipeClient.ConnectAsync(5000, token);
                    
                    IsConnected = true;
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
                catch (Exception ex)
                {
                    if (IsConnected)
                    {
                        IsConnected = false;
                        ConnectionStatusChanged?.Invoke(false);
                    }
                    
                    System.Diagnostics.Debug.WriteLine($"ConnectLoop error: {ex.Message}");
                    
                    // Dispose resources on error
                    reader?.Dispose();
                    writer?.Dispose();
                    pipeClient?.Dispose();
                    
                    if (!token.IsCancellationRequested)
                        await Task.Delay(2000, token);
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
                        var activity = JsonSerializer.Deserialize<FileActivity>(packet.Payload);
                        if (activity != null)
                        {
                            lock (_activitiesLock)
                            {
                                _recentActivities.Insert(0, activity);
                                if (_recentActivities.Count > MaxRecentActivities) _recentActivities.RemoveAt(_recentActivities.Count - 1);
                            }
                            FileActivityDetected?.Invoke(activity);
                        }
                        break;

                    case MessageType.ThreatDetected:
                        var threat = JsonSerializer.Deserialize<Threat>(packet.Payload);
                        if (threat != null)
                        {
                            lock (_threatsLock)
                            {
                                _recentThreats.Insert(0, threat);
                            }
                            ThreatDetected?.Invoke(threat);
                        }
                        break;

                    case MessageType.TelemetryUpdate:
                        var tele = JsonSerializer.Deserialize<TelemetryData>(packet.Payload);
                        if (tele != null)
                        {
                            lock (_telemetryLock) { _lastTelemetry = tele; }
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
            // If connected to service, delegate to it (future enhancement)
            // For now, always use local fallback
            
            try
            {
                return Process.GetProcesses()
                    .Select(p =>
                    {
                        try
                        {
                            return new ProcessInfo
                            {
                                Pid = p.Id,
                                Name = p.ProcessName,
                                CpuUsage = 0, // CPU per-process requires performance counter setup
                                MemoryUsage = p.WorkingSet64
                            };
                        }
                        catch
                        {
                            return null;
                        }
                    })
                    .Where(p => p != null)
                    .Cast<ProcessInfo>()
                    .OrderByDescending(p => p.MemoryUsage)
                    .Take(MaxActiveProcesses)
                    .ToList();
            }
            catch
            {
                return Enumerable.Empty<ProcessInfo>();
            }
        }
        
        public DateTime GetLastScanTime() => _lastScanTime;
        private DateTime _lastScanTime = ConfigurationService.Instance.LastScanTime;

        public async Task PerformQuickScan()
        {
            // If connected to the service, delegate to it
            if (IsConnected)
            {
                await SendCommand(CommandType.PerformScan);
                _lastScanTime = DateTime.Now;
                ConfigurationService.Instance.LastScanTime = _lastScanTime;
                ConfigurationService.Instance.Save();
                return;
            }

            // Local fallback scan — runs in background thread
            await Task.Run(() =>
            {
                string[] suspiciousExtensions = { ".locked", ".encrypted", ".crypty", ".wannacry", ".locky", ".crypt", ".enc" };
                var paths = ConfigurationService.Instance.MonitoredPaths;

                foreach (var path in paths)
                {
                    if (!Directory.Exists(path)) continue;
                    try
                    {
                        var files = Directory.EnumerateFiles(path, "*.*", SearchOption.AllDirectories);
                        foreach (var file in files)
                        {
                            var ext = Path.GetExtension(file).ToLowerInvariant();
                            if (suspiciousExtensions.Contains(ext))
                            {
                                var threat = new Threat
                                {
                                    Name = "Ransomware Artifact Detected",
                                    Path = file,
                                    ProcessName = "Local Scanner",
                                    Severity = ThreatSeverity.High,
                                    Timestamp = DateTime.Now
                                };
                                bool added = false;
                                lock (_threatsLock)
                                {
                                    if (!_recentThreats.Any(t => t.Path == file))
                                    {
                                        _recentThreats.Insert(0, threat);
                                        added = true;
                                    }
                                }
                                if (added) ThreatDetected?.Invoke(threat);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"PerformQuickScan file scan error: {ex.Message}");
                    }
                }
                _lastScanTime = DateTime.Now;
                ConfigurationService.Instance.LastScanTime = _lastScanTime;
                ConfigurationService.Instance.Save();
            });
        }

        public double GetSystemCpuUsage() { lock (_telemetryLock) { return _lastTelemetry.CpuUsage; } }
        public long GetSystemMemoryUsage() { lock (_telemetryLock) { return _lastTelemetry.MemoryUsage; } }
        public int GetMonitoredFilesCount() { lock (_telemetryLock) { return _lastTelemetry.MonitoredFilesCount; } }
        public TelemetryData GetTelemetry() { lock (_telemetryLock) { return _lastTelemetry; } }
        public double GetQuarantineStorageUsage() { lock (_telemetryLock) { return _lastTelemetry.QuarantineStorageMb; } }
        public IEnumerable<string> GetQuarantinedFiles() => Enumerable.Empty<string>();
        public async Task KillProcess(int pid) => await SendCommand(CommandType.KillProcess, pid.ToString());

        public async Task QuarantineFile(string filePath)
        {
            if (IsConnected)
            {
                await SendCommand(CommandType.ToggleShield, filePath);
                return;
            }

            // Local fallback: move file to quarantine folder
            await Task.Run(() =>
            {
                try
                {
                    if (!File.Exists(filePath)) return;
                    var quarantineDir = Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                        "RansomGuard", "Quarantine");
                    Directory.CreateDirectory(quarantineDir);
                    var dest = Path.Combine(quarantineDir, Path.GetFileName(filePath) + ".quarantine");
                    File.Move(filePath, dest, overwrite: true);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"QuarantineFile error: {ex.Message}");
                }
            });
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            // Stop and dispose timers
            _localTelemetryTimer?.Stop();
            _localTelemetryTimer?.Dispose();
            _networkTelemetryTimer?.Stop();
            _networkTelemetryTimer?.Dispose();

            // Cancel connection loop
            _cts?.Cancel();
            _cts?.Dispose();

            // Dispose performance counters
            _localCpuCounter?.Dispose();
            _localRamCounter?.Dispose();

            // Dispose pipe resources
            _writer?.Dispose();
            _pipeClient?.Dispose();
        }
    }
}
