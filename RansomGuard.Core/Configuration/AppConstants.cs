namespace RansomGuard.Core.Configuration
{
    /// <summary>
    /// Centralized configuration constants for the RansomGuard application.
    /// All magic numbers and timing values are defined here for easy tuning and maintenance.
    /// </summary>
    public static class AppConstants
    {
        /// <summary>
        /// General application metadata
        /// </summary>
        public static class General
        {
            public const string AppName = "RGCoreEssentials";
            public const string CompanyName = "InnovativeIO";
        }

        /// <summary>
        /// UI refresh and polling intervals (in milliseconds unless otherwise specified)
        /// </summary>
        public static class Timers
        {
            /// <summary>
            /// Process monitor refresh interval (3 seconds)
            /// Controls how often the process list is refreshed in the UI
            /// </summary>
            public const int ProcessMonitorRefreshSeconds = 3;
            
            /// <summary>
            /// Telemetry collection interval (2000ms = 2 seconds)
            /// Controls how often system metrics are collected by the service
            /// </summary>
            public const int TelemetryCollectionMs = 2000;
            
            /// <summary>
            /// Configuration file change debounce (250ms)
            /// Prevents multiple reloads when config file is saved
            /// </summary>
            public const int ConfigDebounceMs = 250;
            
            /// <summary>
            /// Status bar update interval (3 seconds)
            /// Controls how often the status bar refreshes in the UI
            /// </summary>
            public const int StatusBarUpdateSeconds = 3;
            
            /// <summary>
            /// Dashboard refresh interval (5 seconds)
            /// Controls how often dashboard statistics are updated
            /// </summary>
            public const int DashboardRefreshSeconds = 5;
            
            /// <summary>
            /// IPC heartbeat interval (10 seconds)
            /// Controls how often the client sends heartbeat to the service
            /// </summary>
            public const int IpcHeartbeatMs = 10000;
            
            /// <summary>
            /// Engine cleanup interval (5 minutes = 300000ms)
            /// Controls how often the sentinel engine cleans up caches
            /// </summary>
            public const int EngineCleanupMs = 300000;
            
            /// <summary>
            /// Threat alerts refresh interval (2500ms = 2.5 seconds)
            /// Controls how often the threat alerts view refreshes
            /// </summary>
            public const int ThreatAlertsRefreshMs = 2500;
            
            /// <summary>
            /// Activity buffer processing interval (500ms)
            /// Controls how often buffered file activities are processed for UI display
            /// </summary>
            public const int ActivityBufferMs = 500;
            
            /// <summary>
            /// Dashboard telemetry update interval (2000ms = 2 seconds)
            /// Controls how often dashboard telemetry is updated
            /// </summary>
            public const int DashboardTelemetryMs = 2000;
            
            /// <summary>
            /// Settings save debounce interval (500ms)
            /// Prevents multiple saves when settings are changed rapidly
            /// </summary>
            public const int SettingsDebounceMs = 500;
            
            /// <summary>
            /// Service worker telemetry broadcast interval (1000ms = 1 second)
            /// Controls how often the service broadcasts telemetry updates
            /// </summary>
            public const int ServiceTelemetryBroadcastMs = 1000;
            
            /// <summary>
            /// Heartbeat monitor check interval (10 seconds = 10000ms)
            /// Controls how often the server checks for client timeouts
            /// </summary>
            public const int HeartbeatMonitorMs = 10000;
            
            /// <summary>
            /// Process list broadcast interval (5 seconds = 5000ms)
            /// Controls how often the service broadcasts process list updates
            /// </summary>
            public const int ProcessListBroadcastMs = 5000;
        }

        /// <summary>
        /// Collection size limits to prevent unbounded growth
        /// </summary>
        public static class Limits
        {
            /// <summary>
            /// Maximum in-memory activity history (100 items)
            /// Limits the number of file activities kept in memory
            /// </summary>
            public const int MaxActivityHistory = 100;
            
            /// <summary>
            /// Maximum threat deduplication cache size (1000 items)
            /// Prevents duplicate threat notifications within time window
            /// </summary>
            public const int MaxThreatCacheSize = 1000;
            
            /// <summary>
            /// Maximum debounce cache size (5000 items)
            /// Limits file event deduplication cache in sentinel engine
            /// </summary>
            public const int MaxDebounceCacheSize = 5000;
            
            /// <summary>
            /// Maximum recent threats in UI (100 items)
            /// Limits the number of threats displayed in the UI
            /// </summary>
            public const int MaxRecentThreats = 100;
            
            /// <summary>
            /// Maximum recent activities in UI (150 items)
            /// Limits the number of file activities displayed in the UI
            /// </summary>
            public const int MaxRecentActivities = 150;
            
            /// <summary>
            /// Maximum processed event IDs cache (1000 items)
            /// Prevents duplicate event processing in IPC client
            /// </summary>
            public const int MaxProcessedEventIds = 1000;
            
            /// <summary>
            /// FileSystemWatcher internal buffer size (64KB)
            /// Larger buffer reduces chance of missing file events under high load
            /// </summary>
            public const int FileWatcherBufferSize = 65536;
        }

        /// <summary>
        /// Cleanup and maintenance intervals
        /// </summary>
        public static class Cleanup
        {
            /// <summary>
            /// Debounce cache cleanup interval (5 minutes = 300000ms)
            /// How often to clean up old entries from debounce cache
            /// </summary>
            public const int DebounceCleanupMs = 300000;
            
            /// <summary>
            /// Threat cache age limit (60 minutes)
            /// How long to keep threat entries in deduplication cache
            /// </summary>
            public const int ThreatCacheAgeMinutes = 60;
            
            /// <summary>
            /// Debounce window (1 second)
            /// How long to suppress duplicate file events. Reduced from 10s to improve responsiveness.
            /// </summary>
            public const int DebounceWindowSeconds = 1;
        }

        /// <summary>
        /// IPC (Inter-Process Communication) settings
        /// </summary>
        public static class Ipc
        {
            /// <summary>
            /// Initial retry delay (2 seconds = 2000ms)
            /// Starting delay before retrying failed IPC connection
            /// </summary>
            public const int InitialRetryDelayMs = 2000;
            
            /// <summary>
            /// Maximum retry delay (30 seconds = 30000ms)
            /// Maximum delay between IPC connection retry attempts
            /// </summary>
            public const int MaxRetryDelayMs = 30000;
            
            /// <summary>
            /// Connection timeout (5 seconds = 5000ms)
            /// How long to wait for IPC connection before timing out
            /// </summary>
            public const int ConnectionTimeoutMs = 5000;
            
            /// <summary>
            /// Semaphore timeout during disposal (1 second = 1000ms)
            /// How long to wait for pending writes during shutdown
            /// </summary>
            public const int DisposalSemaphoreTimeoutMs = 1000;
            
            /// <summary>
            /// Client message queue size (2000 messages)
            /// Maximum number of queued messages per client
            /// </summary>
            public const int ClientMessageQueueSize = 2000;
            
            /// <summary>
            /// Message queue high water mark (1800 messages)
            /// When to start dropping oldest messages
            /// </summary>
            public const int MessageQueueHighWaterMark = 1800;
            
            /// <summary>
            /// Client heartbeat timeout (30 seconds)
            /// How long before a client is considered disconnected
            /// Set to 3x the heartbeat interval (10s) for testing single connection fix
            /// If connection remains stable, the duplicate connection was the root cause
            /// </summary>
            public const int ClientHeartbeatTimeoutSeconds = 30;
            
            /// <summary>
            /// Retry delay jitter range (±200ms)
            /// Random variation added to retry delays
            /// </summary>
            public const int RetryDelayJitterMs = 200;
            
            /// <summary>
            /// Minimum retry delay (1 second = 1000ms)
            /// Minimum delay between retry attempts
            /// </summary>
            public const int MinRetryDelayMs = 1000;
            
            /// <summary>
            /// Pipe reconnect delay after error (2 seconds = 2000ms)
            /// Delay before attempting to recreate pipe after error
            /// </summary>
            public const int PipeReconnectDelayMs = 2000;
        }

        /// <summary>
        /// Logging configuration
        /// </summary>
        public static class Logging
        {
            /// <summary>
            /// Maximum log file size (10 MB)
            /// Log files are rotated when they exceed this size
            /// </summary>
            public const long MaxLogSizeBytes = 10 * 1024 * 1024;
            
            /// <summary>
            /// Maximum number of archived log files to keep (5 files)
            /// Older archived logs are deleted automatically
            /// </summary>
            public const int MaxArchivedLogs = 5;
        }

        /// <summary>
        /// Database configuration
        /// </summary>
        public static class Database
        {
            /// <summary>
            /// SQLite connection pool size (10 connections)
            /// Maximum number of pooled database connections
            /// </summary>
            public const int ConnectionPoolSize = 10;
            
            /// <summary>
            /// Default query limit for history (100 records)
            /// Number of records to retrieve by default
            /// </summary>
            public const int DefaultHistoryLimit = 100;
        }

        /// <summary>
        /// Security and threat detection thresholds
        /// </summary>
        public static class Security
        {
            /// <summary>
            /// High entropy threshold (7.0)
            /// Files with entropy above this are considered suspicious
            /// </summary>
            public const double HighEntropyThreshold = 7.0;
            
            /// <summary>
            /// Rapid file modification threshold (10 files)
            /// Number of file modifications that trigger ransomware detection
            /// </summary>
            public const int RapidModificationThreshold = 10;
            
            /// <summary>
            /// Rapid modification time window (5 seconds)
            /// Time window for detecting rapid file modifications
            /// </summary>
            public const int RapidModificationWindowSeconds = 5;
        }
    }
}
