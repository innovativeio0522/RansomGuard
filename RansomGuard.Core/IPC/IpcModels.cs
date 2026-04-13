using System;
using RansomGuard.Core.Models;

namespace RansomGuard.Core.IPC
{
    public enum MessageType
    {
        FileActivity,
        ThreatDetected,
        TelemetryUpdate,
        CommandRequest,
        CommandResponse
    }

    public class IpcPacket
    {
        public MessageType Type { get; set; }
        public string Payload { get; set; } = string.Empty; // JSON serialized
    }

    public class TelemetryData
    {
        public double CpuUsage { get; set; }
        public long MemoryUsage { get; set; }
        public int ProcessesCount { get; set; }
        public int MonitoredFilesCount { get; set; }
        public bool IsHoneyPotActive { get; set; }
        public bool IsVssShieldActive { get; set; }
        public bool IsPanicModeActive { get; set; }
        public int QuarantinedFilesCount { get; set; }
        public double QuarantineStorageMb { get; set; }
    }

    public enum CommandType
    {
        PerformScan,
        KillProcess,
        ToggleShield,
        UpdatePaths
    }

    public class CommandRequest
    {
        public CommandType Command { get; set; }
        public string Arguments { get; set; } = string.Empty;
    }
}
