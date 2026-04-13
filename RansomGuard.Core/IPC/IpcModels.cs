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
        public const int CurrentVersion = 1;
        
        public int Version { get; set; } = CurrentVersion;
        public MessageType Type { get; set; }
        public string Payload { get; set; } = string.Empty; // JSON serialized
    }

    public class TelemetryData
    {
        public double CpuUsage { get; set; }
        public long MemoryUsage { get; set; }
        public double SystemRamUsedMb { get; set; }
        public double SystemRamTotalMb { get; set; }
        public double EntropyScore { get; set; }
        public int ProcessesCount { get; set; }
        public int MonitoredFilesCount { get; set; }
        public bool IsHoneyPotActive { get; set; }
        public bool IsVssShieldActive { get; set; }
        public bool IsPanicModeActive { get; set; }
        public int QuarantinedFilesCount { get; set; }
        public double QuarantineStorageMb { get; set; }

        // Dynamic telemetry fields
        public double NetworkLatencyMs { get; set; }
        public int ActiveEndpointsCount { get; set; }
        public string EncryptionLevel { get; set; } = "AES-256";
        public int FilesPerHour { get; set; }
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
