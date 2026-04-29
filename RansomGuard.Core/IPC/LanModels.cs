using System;
using System.Collections.Generic;

namespace RansomGuard.Core.IPC
{
    /// <summary>
    /// Message types for LAN Circuit Breaker communication.
    /// </summary>
    public enum LanMessageType
    {
        /// <summary>Periodic heartbeat beacon announcing presence on the LAN.</summary>
        Beacon = 0,

        /// <summary>Emergency circuit break signal — triggers critical response on all peers.</summary>
        CircuitBreak = 1,

        /// <summary>Acknowledgement of a received circuit break signal.</summary>
        Acknowledge = 2
    }

    /// <summary>
    /// UDP broadcast packet for LAN peer-to-peer communication.
    /// Serialized as JSON and sent via UDP broadcast on the configured port.
    /// </summary>
    public class LanPacket
    {
        /// <summary>Protocol version for forward compatibility.</summary>
        public int Version { get; set; } = 1;

        /// <summary>Type of LAN message.</summary>
        public LanMessageType Type { get; set; }

        /// <summary>Unique machine identifier (derived from machine GUID).</summary>
        public string NodeId { get; set; } = string.Empty;

        /// <summary>Human-readable machine hostname.</summary>
        public string NodeName { get; set; } = string.Empty;

        /// <summary>RansomGuard application version.</summary>
        public string AppVersion { get; set; } = string.Empty;

        /// <summary>UTC timestamp when the packet was created.</summary>
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Threat details when Type is CircuitBreak.
        /// Contains process name, file count, and severity info.
        /// </summary>
        public string ThreatInfo { get; set; } = string.Empty;

        /// <summary>
        /// HMAC-SHA256 signature for authentication.
        /// Empty string if no shared secret is configured (open trust).
        /// </summary>
        public string Hmac { get; set; } = string.Empty;
    }

    /// <summary>
    /// Represents a discovered RansomGuard peer on the LAN.
    /// </summary>
    public class LanPeer
    {
        /// <summary>Unique machine identifier.</summary>
        public string NodeId { get; set; } = string.Empty;

        /// <summary>Human-readable machine hostname.</summary>
        public string NodeName { get; set; } = string.Empty;

        /// <summary>IP address of the peer.</summary>
        public string IpAddress { get; set; } = string.Empty;

        /// <summary>RansomGuard version running on the peer.</summary>
        public string AppVersion { get; set; } = string.Empty;

        /// <summary>Last time a beacon was received from this peer.</summary>
        public DateTime LastSeen { get; set; }

        /// <summary>Current status: Online, Alert, or Offline.</summary>
        public string Status { get; set; } = "Online";
    }

    /// <summary>
    /// Payload sent to the UI when the LAN peer list changes.
    /// </summary>
    public class LanPeerListUpdate
    {
        /// <summary>List of currently known peers.</summary>
        public List<LanPeer> Peers { get; set; } = new();

        /// <summary>Whether the circuit breaker has been tripped.</summary>
        public bool IsCircuitBroken { get; set; }

        /// <summary>Info about what triggered the break, if any.</summary>
        public string TriggerInfo { get; set; } = string.Empty;
    }
}
