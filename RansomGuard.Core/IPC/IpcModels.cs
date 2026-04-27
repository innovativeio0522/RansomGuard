using System;
using RansomGuard.Core.Models;

namespace RansomGuard.Core.IPC
{
    public enum MessageType
    {
        FileActivity,
        FileActivitySnapshot,
        ThreatDetected,
        ThreatDetectedSnapshot,
        TelemetryUpdate,
        CommandRequest,
        CommandResponse,
        ScanCompleted,
        ProcessListUpdate,
        HandshakeRequest,
        HandshakeResponse,
        Heartbeat,
        Acknowledge
    }

    public class IpcPacket
    {
        public const int CurrentVersion = 1;
        
        public int Version { get; set; } = CurrentVersion;
        public long SequenceId { get; set; } = 0;
        public MessageType Type { get; set; }
        public string Payload { get; set; } = string.Empty; // JSON serialized
    }

    public class TelemetryData
    {
        public double CpuUsage { get; set; }
        public double KernelCpuUsage { get; set; }
        public double UserCpuUsage { get; set; }
        public long MemoryUsage { get; set; }
        public double SystemRamUsedMb { get; set; }
        public double SystemRamTotalMb { get; set; }
        public double EntropyScore { get; set; }
        public int ProcessesCount { get; set; }
        public int ActiveThreadsCount { get; set; }
        public double TrustedProcessPercent { get; set; }
        public int SuspiciousProcessCount { get; set; }
        public int MonitoredFilesCount { get; set; }
        public int ActiveWatchers { get; set; }
        public bool IsHoneyPotActive { get; set; }
        public bool IsVssShieldActive { get; set; }
        public bool IsPanicModeActive { get; set; }
        public int QuarantinedFilesCount { get; set; }
        public double QuarantineStorageMb { get; set; }
        public bool IsRealTimeProtectionEnabled { get; set; }

        // Dynamic telemetry fields
        public double NetworkLatencyMs { get; set; }
        public int ActiveEndpointsCount { get; set; }
        public string EncryptionLevel { get; set; } = "AES-256";
        public int FilesPerHour { get; set; }
        public string[] MonitoredPaths { get; set; } = Array.Empty<string>();
        public DateTime LastScanTime { get; set; } = DateTime.MinValue;
        public int TotalScansCount { get; set; }
    }


    public enum CommandType
    {

        KillProcess,
        ToggleShield,
        UpdatePaths,
        QuarantineFile,
        RestoreFile,
        DeleteFile,
        ClearSafeFiles,
        GetProcessList,
        WhitelistProcess,
        RemoveWhitelist,
        MitigateThreat
    }

    public class CommandRequest
    {
        public CommandType Command { get; set; }
        public string Arguments { get; set; } = string.Empty;
    }

    public class ScanSummary
    {
        public int FilesChecked { get; set; }
        public int ThreatsFound { get; set; }
        public DateTime Timestamp { get; set; } = DateTime.Now;
    }
}
