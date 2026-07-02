namespace RansomGuard.Core.Constants
{
    /// <summary>
    /// Centralized application identifiers and magic strings used throughout RansomGuard.
    /// </summary>
    public static class AppIdentifiers
    {
        // Process Names
        public const string UiProcessName = "RGUI";
        public const string ServiceProcessName = "RGService";
        public const string WatchdogProcessName = "RGWorker";
        
        // Service Names
        public const string ServiceName = "RGService";
        public const string ServiceDisplayName = "RansomGuard Sentinel";
        
        // Task Names
        public const string UiStartupTaskName = "RansomGuardSilentStart";
        public const string WatchdogTaskName = "RGWorkerTask";
        
        // IPC Names
        public const string PipeName = "SentinelGuardPipeV2";
        
        // Mutex Names
        public const string UiMutexName = "RGUI_Identity_Mutex";
        
        // File Names
        public const string ConfigFileName = "config.json";
        public const string LegacyConfigFileName = "config_legacy.json";
        public const string HoneypotFileName = "_000_IMPORTANT_DATA_RECOVERY.docx";
        
        // Log File Names
        public const string AppLogFile = "app.log";
        public const string ConfigLogFile = "config.log";
        public const string IpcClientLogFile = "ipc_client.log";
        public const string SentinelEngineLogFile = "sentinel_engine.log";
        public const string HistoryManagerLogFile = "history_manager.log";
        public const string WatchdogLogFile = "watchdog.log";
        public const string UiErrorLogFile = "ui_error.log";
        public const string ActiveResponseLogFile = "active_response.log";
        public const string HistoryStoreLogFile = "history_store.log";
        public const string SystemLogFile = "system.log";
        public const string VssShieldLogFile = "vss_shield.log";
        public const string CriticalResponseLogFile = "critical_response.log";
        public const string ProcessAttributionLogFile = "process_attribution.log";
        public const string MassEncryptionLogFile = "mass_encryption.log";
        public const string IpcConnectionsLogFile = "ipc_connections.log";
        public const string IpcLogFile = "ipc.log";
        public const string FileMonitoringLogFile = "file_monitoring.log";
        public const string EtwMonitorLogFile = "etw_monitor.log";
        public const string UiCriticalLogFile = "ui_critical.log";
        public const string FirewallLogFile = "firewall.log";
        
        // Command Keywords
        public const string CmdDelete = "delete";
        public const string CmdShadows = "shadows";
        public const string CmdCatalog = "catalog";
        public const string CmdSystemStateBackup = "systemstatebackup";
        public const string CmdSet = "set";
        public const string CmdRecoveryEnabled = "recoveryenabled";
        public const string CmdNo = "no";
        public const string CmdWin32ShadowCopy = "Win32_ShadowCopy";
        public const string CmdRemoveWmiObject = "Remove-WmiObject";
        public const string CmdRemoveCimInstance = "Remove-CimInstance";
        
        // Honeypot Marker
        public const string HoneypotMarker = "!$RansomGuard_Bait";

        // Registry Paths
        public const string RegistryRunKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
        public const string RegistryRunOnceKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\RunOnce";

        // System Utilities
        public const string PowerShellExe = "powershell.exe";
        public const string VssAdminExe = "vssadmin.exe";
        public const string WmicExe = "wmic.exe";
        public const string WbAdminExe = "wbadmin.exe";
        public const string BcdEditExe = "bcdedit.exe";
        public const string ShutdownExe = "shutdown.exe";
        public const string NetshExe = "netsh";
        public const string SchTasksExe = "schtasks.exe";
        public const string ScExe = "sc.exe";
    }
}
