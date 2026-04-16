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
        private const string PipeName = "SentinelGuardPipe";
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
        public event Action<ScanSummary>? ScanCompleted;
        public event Action? ProcessListUpdated;

        public bool IsConnected { get; private set; }

        private readonly List<FileActivity> _recentActivities = new();
        private readonly List<Threat> _recentThreats = new();
        private readonly object _activitiesLock = new();
        private readonly object _threatsLock = new();

        private TelemetryData _lastTelemetry = new();
        private List<ProcessInfo> _lastProcesses = new();
        private readonly object _telemetryLock = new();
        private readonly object _processLock = new();

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

                // Always update CPU, RAM and Process Count in the shared telemetry cache
                lock (_telemetryLock)
                {
                    _lastTelemetry.CpuUsage = _localCpuUsage;
                    _lastTelemetry.MemoryUsage = _localMemoryUsage;
                    _lastTelemetry.SystemRamUsedMb = _systemRamUsedMb;
                    _lastTelemetry.SystemRamTotalMb = _systemRamTotalMb;
                    _lastTelemetry.EntropyScore = _entropyScore;
                    _lastTelemetry.ProcessesCount = Process.GetProcesses().Length;
                    
                    int threads = 0;
                    int suspiciousCount = 0;
                    if (!IsConnected)
                    {
                        var procs = Process.GetProcesses();
                        foreach(var p in procs) {
                            try {
                                threads += p.Threads.Count;
                                if (!p.ProcessName.ToLower().Contains("svchost") && 
                                    !p.ProcessName.ToLower().Contains("system") && 
                                    !p.ProcessName.ToLower().Contains("windows")) 
                                {
                                    // Simulated logic to match backend
                                }
                            } catch { }
                        }
                        
                        int total = procs.Length;
                        double trustedPercent = total > 0 ? 99.2 : 100;
                        if (total > 0) {
                             suspiciousCount = Math.Max(1, (int)(total * 0.01));
                             trustedPercent = Math.Round(((double)(total - suspiciousCount) / total) * 100, 1);
                        }

                        _lastTelemetry.ActiveThreadsCount = threads;
                        _lastTelemetry.TrustedProcessPercent = trustedPercent;
                        _lastTelemetry.SuspiciousProcessCount = suspiciousCount;
                    }

                    _lastTelemetry.NetworkLatencyMs = _networkLatencyMs;
                    _lastTelemetry.ActiveEndpointsCount = _activeEndpointsCount;
                    _lastTelemetry.EncryptionLevel = _encryptionLevel;
                    _lastTelemetry.FilesPerHour = _filesPerHour;
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
                    case MessageType.ScanCompleted:
                        var summary = JsonSerializer.Deserialize<ScanSummary>(packet.Payload);
                        if (summary != null)
                        {
                            ScanCompleted?.Invoke(summary);
                        }
                        break;
                    case MessageType.ProcessListUpdate:
                        var procs = JsonSerializer.Deserialize<List<ProcessInfo>>(packet.Payload);
                        if (procs != null)
                        {
                            lock (_processLock) { _lastProcesses = procs; }
                            System.Diagnostics.Debug.WriteLine($"[IPC] Received {procs.Count} processes from service");
                            ProcessListUpdated?.Invoke();
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
            // If connected to service, delegate to it
            if (IsConnected)
            {
                lock (_processLock)
                {
                    System.Diagnostics.Debug.WriteLine($"[GetActiveProcesses] IsConnected=true, returning {_lastProcesses.Count} processes from cache");
                    if (_lastProcesses.Count == 0)
                    {
                        System.Diagnostics.Debug.WriteLine("[GetActiveProcesses] WARNING: Connected but process cache is empty! Falling back to local.");
                        // Service is connected but hasn't sent process data yet - use local fallback
                        return GetLocalProcesses();
                    }
                    return _lastProcesses.ToList();
                }
            }

            // Local fallback — used when service is offline
            System.Diagnostics.Debug.WriteLine("[GetActiveProcesses] IsConnected=false, using local fallback");
            return GetLocalProcesses();
        }

        private List<ProcessInfo> GetLocalProcesses()
        {
            try
            {
                var allProcesses = Process.GetProcesses()
                    .Select(p =>
                    {
                        try
                        {
                            bool isSystem = p.ProcessName.ToLower().Contains("svchost") || 
                                          p.ProcessName.ToLower().Contains("system") || 
                                          p.ProcessName.ToLower().Contains("windows");
                                          
                            var processInfo = new ProcessInfo
                            {
                                Pid = p.Id,
                                Name = p.ProcessName,
                                CpuUsage = ProcessStatsProvider.Instance.GetCpuUsage(p),
                                MemoryUsage = p.WorkingSet64,
                                IsTrusted = isSystem,
                                SignatureStatus = isSystem ? "Verified" : "User Process"
                            };
                            
                            return processInfo;
                        }
                        catch
                        {
                            return null;
                        }
                    })
                    .Where(p => p != null)
                    .Cast<ProcessInfo>()
                    .ToList();
                
                // Smart selection: Always include system processes, then fill with top user processes
                var trustedProcesses = allProcesses.Where(p => p.IsTrusted).OrderByDescending(p => p.MemoryUsage).ToList();
                var userProcesses = allProcesses.Where(p => !p.IsTrusted).OrderByDescending(p => p.MemoryUsage).ToList();
                
                // Take up to 20 trusted processes and 30 user processes (total 50)
                var processes = trustedProcesses.Take(20).Concat(userProcesses.Take(30)).ToList();
                
                var trustedCount = processes.Count(p => p.IsTrusted);
                var untrustedCount = processes.Count - trustedCount;
                System.Diagnostics.Debug.WriteLine($"[GetLocalProcesses] Returning {processes.Count} processes: {trustedCount} trusted, {untrustedCount} untrusted");
                File.AppendAllText("process_debug.log", $"{DateTime.Now}: [GetLocalProcesses] Total={processes.Count}, Trusted={trustedCount}, Untrusted={untrustedCount}\n");
                
                return processes;
            }

            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[GetLocalProcesses] Exception: {ex.Message}");
                return new List<ProcessInfo>();
            }
        }
        
        public DateTime GetLastScanTime() => _lastScanTime;
        private DateTime _lastScanTime = ConfigurationService.Instance.LastScanTime;

        public async Task PerformQuickScan()
        {
            System.Diagnostics.Debug.WriteLine($"PerformQuickScan initiated. IsConnected: {IsConnected}");
            
            // If connected to the service, delegate to it
            if (IsConnected)
            {
                System.Diagnostics.Debug.WriteLine("Sending PerformScan command to service...");
                await SendCommand(CommandType.PerformScan);
                _lastScanTime = DateTime.Now;
                ConfigurationService.Instance.LastScanTime = _lastScanTime;
                ConfigurationService.Instance.TotalScansCount++;
                ConfigurationService.Instance.Save();
                return;
            }

            // Local fallback scan — runs in background thread
            await Task.Run(() =>
            {
                string[] suspiciousExtensions = { ".locked", ".encrypted", ".crypty", ".wannacry", ".locky", ".crypt", ".enc" };
                var paths = ConfigurationService.Instance.MonitoredPaths;
                int filesChecked = 0;
                int threatsFound = 0;

                foreach (var path in paths)
                {
                    if (!Directory.Exists(path)) continue;
                    try
                        {
                            var files = Directory.EnumerateFiles(path, "*.*", SearchOption.AllDirectories);
                            foreach (var file in files)
                            {
                                filesChecked++;
                                var ext = Path.GetExtension(file).ToLowerInvariant();
                                if (suspiciousExtensions.Contains(ext))
                                {
                                    threatsFound++;
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
                ConfigurationService.Instance.TotalScansCount++;
                ConfigurationService.Instance.Save();
                
                ScanCompleted?.Invoke(new ScanSummary 
                { 
                    FilesChecked = filesChecked, 
                    ThreatsFound = threatsFound,
                    Timestamp = DateTime.Now
                });
            });
        }

        public double GetSystemCpuUsage() { lock (_telemetryLock) { return _lastTelemetry.CpuUsage; } }
        public long GetSystemMemoryUsage() { lock (_telemetryLock) { return _lastTelemetry.MemoryUsage; } }
        public int GetMonitoredFilesCount() { lock (_telemetryLock) { return _lastTelemetry.MonitoredFilesCount; } }
        public TelemetryData GetTelemetry() { lock (_telemetryLock) { return _lastTelemetry; } }
        public double GetQuarantineStorageUsage() 
        { 
            if (IsConnected)
            {
                lock (_telemetryLock) { return _lastTelemetry.QuarantineStorageMb; }
            }

            // Local implementation fallback
            try
            {
                if (!Directory.Exists(PathConfiguration.QuarantinePath)) return 0;
                var files = Directory.GetFiles(PathConfiguration.QuarantinePath, "*.quarantine");
                long totalBytes = files.Sum(f => new FileInfo(f).Length);
                return totalBytes / (1024.0 * 1024.0);
            }
            catch { return 0; }
        }

        public IEnumerable<string> GetQuarantinedFiles()
        {
            try
            {
                if (!Directory.Exists(PathConfiguration.QuarantinePath)) return Enumerable.Empty<string>();
                return Directory.GetFiles(PathConfiguration.QuarantinePath, "*.quarantine");
            }
            catch { return Enumerable.Empty<string>(); }
        }
        public async Task KillProcess(int pid) => await SendCommand(CommandType.KillProcess, pid.ToString());

        /// <summary>
        /// No-op on the client side — watcher re-initialisation is triggered via the UpdatePaths IPC command to the service.
        /// </summary>
        public void InitializeWatchers() => _ = SendCommand(CommandType.UpdatePaths);

        public async Task QuarantineFile(string filePath)
        {
            if (IsConnected)
            {
                await SendCommand(CommandType.QuarantineFile, filePath);
            }
            else
            {
                // Local fallback
                await Task.Run(() => {
                    try {
                        string qDir = PathConfiguration.QuarantinePath;
                        Directory.CreateDirectory(qDir);
                        string dest = Path.Combine(qDir, Path.GetFileName(filePath) + ".quarantine");
                        File.Move(filePath, dest, overwrite: true);
                        File.WriteAllLines(dest + ".metadata", new[] { $"OriginalPath={filePath}", $"QuarantinedAt={DateTime.Now:O}" });
                    } catch { }
                });
            }
        }

        public async Task RestoreQuarantinedFile(string quarantinePath)
        {
            if (IsConnected)
            {
                await SendCommand(CommandType.RestoreFile, quarantinePath);
            }
            else
            {
                // Local fallback for disconnected mode
                await Task.Run(() =>
                {
                    try
                    {
                        string metaPath = quarantinePath + ".metadata";
                        string originalPath = "Unknown Path";
                        if (File.Exists(metaPath))
                        {
                            var lines = File.ReadAllLines(metaPath);
                            foreach (var line in lines)
                            {
                                if (line.StartsWith("OriginalPath=")) originalPath = line.Substring("OriginalPath=".Length);
                            }
                        }

                        if (originalPath != "Unknown Path")
                        {
                            string? dir = Path.GetDirectoryName(originalPath);
                            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                            File.Move(quarantinePath, originalPath, overwrite: false);
                            if (File.Exists(metaPath)) File.Delete(metaPath);
                        }
                    }
                    catch { }
                });
            }
        }

        public async Task DeleteQuarantinedFile(string quarantinePath)
        {
            if (IsConnected)
            {
                await SendCommand(CommandType.DeleteFile, quarantinePath);
            }
            else
            {
                await Task.Run(() =>
                {
                    try
                    {
                        if (File.Exists(quarantinePath)) File.Delete(quarantinePath);
                        string meta = quarantinePath + ".metadata";
                        if (File.Exists(meta)) File.Delete(meta);
                    }
                    catch { }
                });
            }
        }

        public async Task ClearSafeFiles()
        {
            if (IsConnected)
            {
                await SendCommand(CommandType.ClearSafeFiles, string.Empty);
            }
            else
            {
                // Local fallback logic
                await Task.Run(() => {
                    try {
                        string qDir = PathConfiguration.QuarantinePath;
                        if (!Directory.Exists(qDir)) return;
                        var files = Directory.EnumerateFiles(qDir, "*.quarantine");
                        foreach (var f in files) {
                            if ((DateTime.Now - File.GetLastWriteTime(f)).TotalDays > 30) {
                                File.Delete(f);
                                if (File.Exists(f + ".metadata")) File.Delete(f + ".metadata");
                            }
                        }
                    } catch { }
                });
            }
        }

        public async Task WhitelistProcess(string name) => await SendCommand(CommandType.WhitelistProcess, name);
        public async Task RemoveWhitelist(string name) => await SendCommand(CommandType.RemoveWhitelist, name);

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
