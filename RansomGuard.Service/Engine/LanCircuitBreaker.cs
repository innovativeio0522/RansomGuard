using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using RansomGuard.Core.Helpers;
using RansomGuard.Core.IPC;
using RansomGuard.Core.Services;
using RansomGuard.Core.Constants;
using RansomGuard.Core.Configuration;

namespace RansomGuard.Service.Engine
{
    /// <summary>
    /// LAN Circuit Breaker — Distributed ransomware defense.
    /// Broadcasts beacons to discover peers and sends circuit break signals
    /// when mass encryption is detected, triggering critical response on all LAN nodes.
    /// </summary>
    public class LanCircuitBreaker : IDisposable
    {
        private readonly ConcurrentDictionary<string, LanPeer> _peers = new();
        private readonly string _nodeId;
        private readonly string _nodeName;
        private readonly string _appVersion;

        private UdpClient? _udpClient;
        private CancellationTokenSource? _cts;
        private Task? _beaconTask;
        private Task? _listenerTask;
        private Task? _cleanupTask;
        private bool _disposed;
        private bool _isStarted;
        private volatile bool _isCircuitBroken;  // volatile: read/written from multiple threads
        private volatile string _triggerInfo = string.Empty;
        private readonly object _lifecycleLock = new();
        private readonly object _circuitBreakLock = new(); // guards _isCircuitBroken + _triggerInfo writes

        private const int BeaconIntervalMs = AppConstants.Timers.LanBeaconIntervalMs;
        private const int PeerTimeoutSeconds = AppConstants.Limits.LanPeerTimeoutSeconds;
        private const int CleanupIntervalMs = AppConstants.Timers.LanCleanupIntervalMs;

        /// <summary>Raised when the peer list changes (peer added, removed, or status changed).</summary>
        public event Action<LanPeerListUpdate>? PeerListChanged;

        /// <summary>Raised when a circuit break signal is received from a peer.</summary>
        public event Action<string>? CircuitBreakReceived;

        /// <summary>Number of currently known online peers.</summary>
        public int PeerCount => _peers.Count;

        /// <summary>Whether the circuit breaker has been tripped.</summary>
        public bool IsCircuitBroken => _isCircuitBroken;

        public LanCircuitBreaker()
        {
            _nodeId = GetMachineId();
            _nodeName = Environment.MachineName;
            _appVersion = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "1.0.0.0";
        }

        /// <summary>
        /// Starts the beacon broadcaster, UDP listener, and peer cleanup tasks.
        /// </summary>
        public void Start()
        {
            lock (_lifecycleLock)
            {
                if (_disposed)
                    return;

                if (_isStarted)
                {
                    FileLogger.Log(AppIdentifiers.SentinelEngineLogFile, "[LAN] Circuit Breaker already started.");
                    return;
                }

                var config = ConfigurationService.Instance;
                if (!config.LanCircuitBreakerEnabled)
                {
                    FileLogger.Log(AppIdentifiers.SentinelEngineLogFile, "[LAN] Circuit Breaker is DISABLED in settings.");
                    return;
                }

                int port = config.LanBroadcastPort;
                if (port is < 1 or > 65535)
                {
                    FileLogger.LogError(AppIdentifiers.SentinelEngineLogFile, $"[LAN] Invalid broadcast port configured: {port}");
                    return;
                }

                try
                {
                    FileLogger.Log(AppIdentifiers.SentinelEngineLogFile, $"[LAN] Ensuring firewall rules for UDP port {port} via runtime service.");
                    bool firewallConfigured = FirewallManager.EnsureLanFirewallRules(port);

                    if (!firewallConfigured)
                    {
                        FileLogger.LogError(AppIdentifiers.SentinelEngineLogFile, "[LAN] Firewall rules were not verified. LAN discovery may be blocked by Windows Firewall.");
                    }

                    _udpClient = new UdpClient(AddressFamily.InterNetwork);
                    _udpClient.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                    _udpClient.Client.Bind(new IPEndPoint(IPAddress.Any, port));
                    _udpClient.EnableBroadcast = true;

                    _cts = new CancellationTokenSource();
                    var token = _cts.Token;
                    _beaconTask = Task.Run(() => BeaconLoopAsync(token), token);
                    _listenerTask = Task.Run(() => ListenerLoopAsync(token), token);
                    _cleanupTask = Task.Run(() => CleanupLoopAsync(token), token);
                    _isStarted = true;

                    FileLogger.Log(AppIdentifiers.SentinelEngineLogFile, $"[LAN] Circuit Breaker STARTED on UDP port {port}. NodeId={_nodeId}, NodeName={_nodeName}");
                }
                catch (Exception ex)
                {
                    CleanupSocket();
                    FileLogger.LogError(AppIdentifiers.SentinelEngineLogFile, $"[LAN] Failed to start Circuit Breaker: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// Stops all tasks and releases the UDP socket.
        /// </summary>
        public void Stop()
        {
            lock (_lifecycleLock)
            {
                if (!_isStarted && _udpClient == null && _cts == null)
                    return;

                CleanupSocket();
                FileLogger.Log(AppIdentifiers.SentinelEngineLogFile, "[LAN] Circuit Breaker STOPPED.");
            }
        }

        /// <summary>
        /// Broadcasts a CIRCUIT_BREAK signal to all LAN peers.
        /// Called by SentinelEngine when mass encryption is detected.
        /// </summary>
        public async Task TriggerCircuitBreakAsync(string threatInfo)
        {
            if (_disposed || _udpClient == null) return;

            lock (_circuitBreakLock)
            {
                _isCircuitBroken = true;
                _triggerInfo = threatInfo;
            }

            var packet = CreatePacket(LanMessageType.CircuitBreak);
            packet.ThreatInfo = threatInfo;

            try
            {
                var json = JsonSerializer.Serialize(packet);
                var bytes = Encoding.UTF8.GetBytes(json);
                var port = ConfigurationService.Instance.LanBroadcastPort;
                var endpoints = GetBroadcastEndpoints(port);

                // Send 3 times for reliability (UDP is unreliable)
                for (int i = 0; i < 3; i++)
                {
                    foreach (var endpoint in endpoints)
                    {
                        try
                        {
                            await _udpClient.SendAsync(bytes, bytes.Length, endpoint).ConfigureAwait(false);
                        }
                        catch
                        {
                            // Suppress errors for specific endpoints to ensure other endpoints get sent
                        }
                    }
                    if (i < 2) await Task.Delay(50).ConfigureAwait(false);
                }

                FileLogger.Log(AppIdentifiers.SentinelEngineLogFile, $"[LAN] CIRCUIT_BREAK broadcast sent to {endpoints.Count} endpoints! Threat: {threatInfo}");
                NotifyPeerListChanged();
            }
            catch (Exception ex)
            {
                FileLogger.LogError(AppIdentifiers.SentinelEngineLogFile, $"[LAN] Failed to broadcast CIRCUIT_BREAK: {ex.Message}");
            }
        }

        /// <summary>
        /// Resets the circuit breaker after a manual user action.
        /// </summary>
        public void ResetCircuitBreaker()
        {
            lock (_circuitBreakLock)
            {
                _isCircuitBroken = false;
                _triggerInfo = string.Empty;
            }
            FileLogger.Log(AppIdentifiers.SentinelEngineLogFile, "[LAN] Circuit Breaker RESET by user.");
            NotifyPeerListChanged();
        }

        #region Background Loops

        private async Task BeaconLoopAsync(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    var packet = CreatePacket(LanMessageType.Beacon);
                    var json = JsonSerializer.Serialize(packet);
                    var bytes = Encoding.UTF8.GetBytes(json);
                    var port = ConfigurationService.Instance.LanBroadcastPort;
                    var endpoints = GetBroadcastEndpoints(port);

                    foreach (var endpoint in endpoints)
                    {
                        try
                        {
                            _udpClient?.Send(bytes, bytes.Length, endpoint);
                        }
                        catch
                        {
                            // Suppress errors for specific endpoints to ensure other endpoints get sent
                        }
                    }
                }
                catch (OperationCanceledException) { break; }
                catch (ObjectDisposedException) { break; }
                catch (Exception ex)
                {
                    FileLogger.LogError(AppIdentifiers.SentinelEngineLogFile, $"[LAN] Beacon send error: {ex.Message}");
                }

                try
                {
                    await Task.Delay(BeaconIntervalMs, token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }

        private async Task ListenerLoopAsync(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    if (_udpClient == null) break;

                    var result = await _udpClient.ReceiveAsync(token).ConfigureAwait(false);
                    var json = Encoding.UTF8.GetString(result.Buffer);

                    LanPacket? packet;
                    try
                    {
                        packet = JsonSerializer.Deserialize<LanPacket>(json);
                    }
                    catch
                    {
                        continue; // Ignore malformed packets
                    }

                    if (packet == null || packet.NodeId == _nodeId) continue; // Ignore own packets

                    // Validate HMAC if shared secret is configured
                    if (!ValidateHmac(packet, json))
                    {
                        FileLogger.LogError(AppIdentifiers.SentinelEngineLogFile, $"[LAN] HMAC validation FAILED from {packet.NodeName} ({result.RemoteEndPoint.Address}). Ignoring.");
                        continue;
                    }
                    
                    // SECURITY: Replay Protection
                    // Validate that the packet was created within the last 60 seconds
                    var drift = Math.Abs((DateTime.UtcNow - packet.Timestamp).TotalSeconds);
                    if (drift > 60)
                    {
                        FileLogger.LogError(AppIdentifiers.SentinelEngineLogFile, $"[LAN] REPLAY ATTACK BLOCKED from {packet.NodeName} ({result.RemoteEndPoint.Address}). Timestamp drift: {drift:F1}s");
                        continue;
                    }

                    switch (packet.Type)
                    {
                        case LanMessageType.Beacon:
                            HandleBeacon(packet, result.RemoteEndPoint.Address.ToString());
                            break;

                        case LanMessageType.CircuitBreak:
                            HandleCircuitBreak(packet, result.RemoteEndPoint.Address.ToString());
                            break;
                    }
                }
                catch (OperationCanceledException) { break; }
                catch (ObjectDisposedException) { break; }
                catch (Exception ex)
                {
                    FileLogger.LogError(AppIdentifiers.SentinelEngineLogFile, $"[LAN] Listener error: {ex.Message}");
                    if (!token.IsCancellationRequested)
                        await Task.Delay(1000, token).ConfigureAwait(false);
                }
            }
        }

        private async Task CleanupLoopAsync(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(CleanupIntervalMs, token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }

                bool changed = false;
                var now = DateTime.UtcNow;

                // Snapshot keys first — ConcurrentDictionary is safe to enumerate but
                // snapshotting avoids processing entries added after we started cleanup
                foreach (var key in _peers.Keys.ToArray())
                {
                    if (_peers.TryGetValue(key, out var peer) &&
                        (now - peer.LastSeen).TotalSeconds > PeerTimeoutSeconds)
                    {
                        if (_peers.TryRemove(key, out _))
                        {
                            FileLogger.Log(AppIdentifiers.SentinelEngineLogFile, $"[LAN] Peer timed out: {peer.NodeName} ({peer.IpAddress})");
                            changed = true;
                        }
                    }
                }

                if (changed) NotifyPeerListChanged();
            }
        }

        #endregion

        #region Message Handlers

        private void HandleBeacon(LanPacket packet, string ipAddress)
        {
            bool isNew = !_peers.ContainsKey(packet.NodeId);

            _peers.AddOrUpdate(packet.NodeId,
                _ => new LanPeer
                {
                    NodeId = packet.NodeId,
                    NodeName = packet.NodeName,
                    IpAddress = ipAddress,
                    AppVersion = packet.AppVersion,
                    LastSeen = DateTime.UtcNow,
                    Status = "Online"
                },
                (_, existing) =>
                {
                    existing.LastSeen = DateTime.UtcNow;
                    existing.IpAddress = ipAddress;
                    existing.AppVersion = packet.AppVersion;
                    if (existing.Status == "Offline") existing.Status = "Online";
                    return existing;
                });

            if (isNew)
            {
                FileLogger.Log(AppIdentifiers.SentinelEngineLogFile, $"[LAN] New peer discovered: {packet.NodeName} ({ipAddress}) v{packet.AppVersion}");
                NotifyPeerListChanged();
            }
        }

        private void HandleCircuitBreak(LanPacket packet, string ipAddress)
        {
            FileLogger.Log(AppIdentifiers.SentinelEngineLogFile,
                $"[LAN] *** CIRCUIT_BREAK RECEIVED from {packet.NodeName} ({ipAddress}) *** Threat: {packet.ThreatInfo}");

            string triggerInfo;
            lock (_circuitBreakLock)
            {
                _isCircuitBroken = true;
                _triggerInfo = $"Remote alert from {packet.NodeName}: {packet.ThreatInfo}";
                triggerInfo = _triggerInfo;
            }

            // Update peer status to Alert
            if (_peers.TryGetValue(packet.NodeId, out var peer))
            {
                peer.Status = "Alert";
            }

            NotifyPeerListChanged();

            // Fire event — SentinelEngine will call ExecuteCriticalResponse()
            CircuitBreakReceived?.Invoke(triggerInfo);
        }

        #endregion

        #region HMAC Authentication

        private LanPacket CreatePacket(LanMessageType type)
        {
            var packet = new LanPacket
            {
                Type = type,
                NodeId = _nodeId,
                NodeName = _nodeName,
                AppVersion = _appVersion,
                Timestamp = DateTime.UtcNow
            };

            var secret = ConfigurationService.Instance.LanSharedSecret;
            if (!string.IsNullOrEmpty(secret))
            {
                packet.Hmac = ComputeHmac(packet, secret);
            }

            return packet;
        }

        private bool ValidateHmac(LanPacket packet, string rawJson)
        {
            var secret = ConfigurationService.Instance.LanSharedSecret;
            if (string.IsNullOrEmpty(secret)) return true; // Open mode — trust all

            if (string.IsNullOrEmpty(packet.Hmac)) return false; // Secret configured but no HMAC in packet

            var expected = ComputeHmac(packet, secret);
            return string.Equals(packet.Hmac, expected, StringComparison.Ordinal);
        }

        private static string ComputeHmac(LanPacket packet, string secret)
        {
            // HMAC over the deterministic fields (excluding the Hmac field itself)
            var payload = $"{packet.Version}|{packet.Type}|{packet.NodeId}|{packet.NodeName}|{packet.Timestamp:O}|{packet.ThreatInfo}";
            using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
            var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(payload));
            return Convert.ToBase64String(hash);
        }

        #endregion

        #region Helpers

        private void NotifyPeerListChanged()
        {
            bool isCircuitBroken;
            string triggerInfo;
            lock (_circuitBreakLock)
            {
                isCircuitBroken = _isCircuitBroken;
                triggerInfo = _triggerInfo;
            }

            var update = new LanPeerListUpdate
            {
                Peers = _peers.Values.ToList(),
                IsCircuitBroken = isCircuitBroken,
                TriggerInfo = triggerInfo
            };
            PeerListChanged?.Invoke(update);
        }

        private void CleanupSocket()
        {
            _isStarted = false;

            try { _cts?.Cancel(); } catch { }
            try { _udpClient?.Close(); } catch { }
            try { _udpClient?.Dispose(); } catch { }
            try { _cts?.Dispose(); } catch { }

            _udpClient = null;
            _cts = null;
            _beaconTask = null;
            _listenerTask = null;
            _cleanupTask = null;
        }

        private static List<IPEndPoint> GetBroadcastEndpoints(int port)
        {
            var endpoints = new List<IPEndPoint>();
            try
            {
                foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (ni.OperationalStatus != OperationalStatus.Up)
                        continue;

                    if (ni.NetworkInterfaceType == NetworkInterfaceType.Loopback ||
                        ni.NetworkInterfaceType == NetworkInterfaceType.Tunnel)
                        continue;

                    var ipProps = ni.GetIPProperties();
                    foreach (var unicast in ipProps.UnicastAddresses)
                    {
                        if (unicast.Address.AddressFamily == AddressFamily.InterNetwork)
                        {
                            var ip = unicast.Address;
                            var mask = unicast.IPv4Mask;
                            if (mask != null)
                            {
                                var ipBytes = ip.GetAddressBytes();
                                var maskBytes = mask.GetAddressBytes();
                                if (ipBytes.Length == maskBytes.Length)
                                {
                                    var broadcastBytes = new byte[ipBytes.Length];
                                    for (int i = 0; i < ipBytes.Length; i++)
                                    {
                                        broadcastBytes[i] = (byte)(ipBytes[i] | ~maskBytes[i]);
                                    }
                                    var broadcastIp = new IPAddress(broadcastBytes);
                                    endpoints.Add(new IPEndPoint(broadcastIp, port));
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                FileLogger.LogError(AppIdentifiers.SentinelEngineLogFile, $"[LAN] Error getting broadcast endpoints: {ex.Message}");
            }

            // Always include global broadcast as fallback
            endpoints.Add(new IPEndPoint(IPAddress.Broadcast, port));

            // De-duplicate endpoints by string representation
            return endpoints.GroupBy(e => e.ToString()).Select(g => g.First()).ToList();
        }

        private static string GetMachineId()
        {
            try
            {
                // Use a hash of the machine name + OS install date for a stable unique ID
                var raw = $"{Environment.MachineName}-{Environment.OSVersion.VersionString}";
                using var sha = SHA256.Create();
                var hash = sha.ComputeHash(Encoding.UTF8.GetBytes(raw));
                return Convert.ToBase64String(hash).Substring(0, 12);
            }
            catch
            {
                return Guid.NewGuid().ToString("N").Substring(0, 12);
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            Stop();
            _cts?.Dispose();
        }

        #endregion
    }
}
