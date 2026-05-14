using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using RansomGuard.Core.Helpers;
using RansomGuard.Core.IPC;
using RansomGuard.Core.Services;

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
        private bool _isCircuitBroken;
        private string _triggerInfo = string.Empty;
        private readonly object _lifecycleLock = new();

        private const int BeaconIntervalMs = 5000;
        private const int PeerTimeoutSeconds = 15;
        private const int CleanupIntervalMs = 5000;

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
            _appVersion = "1.0.1.17";
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
                    FileLogger.Log("sentinel_engine.log", "[LAN] Circuit Breaker already started.");
                    return;
                }

                var config = ConfigurationService.Instance;
                if (!config.LanCircuitBreakerEnabled)
                {
                    FileLogger.Log("sentinel_engine.log", "[LAN] Circuit Breaker is DISABLED in settings.");
                    return;
                }

                int port = config.LanBroadcastPort;
                if (port is < 1 or > 65535)
                {
                    FileLogger.LogError("sentinel_engine.log", $"[LAN] Invalid broadcast port configured: {port}");
                    return;
                }

                try
                {
                    FileLogger.Log("sentinel_engine.log", $"[LAN] Ensuring firewall rules for UDP port {port} via runtime service.");
                    bool firewallConfigured = FirewallManager.EnsureLanFirewallRules(port);

                    if (!firewallConfigured)
                    {
                        FileLogger.LogError("sentinel_engine.log", "[LAN] Firewall rules were not verified. LAN discovery may be blocked by Windows Firewall.");
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

                    FileLogger.Log("sentinel_engine.log", $"[LAN] Circuit Breaker STARTED on UDP port {port}. NodeId={_nodeId}, NodeName={_nodeName}");
                }
                catch (Exception ex)
                {
                    CleanupSocket();
                    FileLogger.LogError("sentinel_engine.log", $"[LAN] Failed to start Circuit Breaker: {ex.Message}");
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
                FileLogger.Log("sentinel_engine.log", "[LAN] Circuit Breaker STOPPED.");
            }
        }

        /// <summary>
        /// Broadcasts a CIRCUIT_BREAK signal to all LAN peers.
        /// Called by SentinelEngine when mass encryption is detected.
        /// </summary>
        public void TriggerCircuitBreak(string threatInfo)
        {
            if (_disposed || _udpClient == null) return;

            _isCircuitBroken = true;
            _triggerInfo = threatInfo;

            var packet = CreatePacket(LanMessageType.CircuitBreak);
            packet.ThreatInfo = threatInfo;

            try
            {
                var json = JsonSerializer.Serialize(packet);
                var bytes = Encoding.UTF8.GetBytes(json);
                var port = ConfigurationService.Instance.LanBroadcastPort;
                var endpoint = new IPEndPoint(IPAddress.Broadcast, port);

                // Send 3 times for reliability (UDP is unreliable)
                for (int i = 0; i < 3; i++)
                {
                    _udpClient.Send(bytes, bytes.Length, endpoint);
                    Thread.Sleep(50);
                }

                FileLogger.Log("sentinel_engine.log", $"[LAN] CIRCUIT_BREAK broadcast sent! Threat: {threatInfo}");
                NotifyPeerListChanged();
            }
            catch (Exception ex)
            {
                FileLogger.LogError("sentinel_engine.log", $"[LAN] Failed to broadcast CIRCUIT_BREAK: {ex.Message}");
            }
        }

        /// <summary>
        /// Resets the circuit breaker after a manual user action.
        /// </summary>
        public void ResetCircuitBreaker()
        {
            _isCircuitBroken = false;
            _triggerInfo = string.Empty;
            FileLogger.Log("sentinel_engine.log", "[LAN] Circuit Breaker RESET by user.");
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
                    var endpoint = new IPEndPoint(IPAddress.Broadcast, port);

                    _udpClient?.Send(bytes, bytes.Length, endpoint);
                }
                catch (OperationCanceledException) { break; }
                catch (ObjectDisposedException) { break; }
                catch (Exception ex)
                {
                    FileLogger.LogError("sentinel_engine.log", $"[LAN] Beacon send error: {ex.Message}");
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
                        FileLogger.LogError("sentinel_engine.log", $"[LAN] HMAC validation FAILED from {packet.NodeName} ({result.RemoteEndPoint.Address}). Ignoring.");
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
                    FileLogger.LogError("sentinel_engine.log", $"[LAN] Listener error: {ex.Message}");
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

                foreach (var kvp in _peers)
                {
                    if ((now - kvp.Value.LastSeen).TotalSeconds > PeerTimeoutSeconds)
                    {
                        if (_peers.TryRemove(kvp.Key, out _))
                        {
                            FileLogger.Log("sentinel_engine.log", $"[LAN] Peer timed out: {kvp.Value.NodeName} ({kvp.Value.IpAddress})");
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
                FileLogger.Log("sentinel_engine.log", $"[LAN] New peer discovered: {packet.NodeName} ({ipAddress}) v{packet.AppVersion}");
                NotifyPeerListChanged();
            }
        }

        private void HandleCircuitBreak(LanPacket packet, string ipAddress)
        {
            FileLogger.Log("sentinel_engine.log",
                $"[LAN] *** CIRCUIT_BREAK RECEIVED from {packet.NodeName} ({ipAddress}) *** Threat: {packet.ThreatInfo}");

            _isCircuitBroken = true;
            _triggerInfo = $"Remote alert from {packet.NodeName}: {packet.ThreatInfo}";

            // Update peer status to Alert
            if (_peers.TryGetValue(packet.NodeId, out var peer))
            {
                peer.Status = "Alert";
            }

            NotifyPeerListChanged();

            // Fire event — SentinelEngine will call ExecuteCriticalResponse()
            CircuitBreakReceived?.Invoke(_triggerInfo);
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
            var update = new LanPeerListUpdate
            {
                Peers = _peers.Values.ToList(),
                IsCircuitBroken = _isCircuitBroken,
                TriggerInfo = _triggerInfo
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
