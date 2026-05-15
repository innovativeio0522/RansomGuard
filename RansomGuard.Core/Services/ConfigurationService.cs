using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using RansomGuard.Core.Helpers;

namespace RansomGuard.Core.Services
{
    /// <summary>
    /// Manages application configuration settings including monitored paths, sensitivity levels, and protection options.
    /// This is a thread-safe singleton service that persists settings to disk.
    /// </summary>
    public class ConfigurationService
    {
        private static readonly Lazy<ConfigurationService> _instance = new Lazy<ConfigurationService>(() => Load(), isThreadSafe: true);
        
        /// <summary>
        /// Gets the singleton instance of the configuration service.
        /// </summary>
        public static ConfigurationService Instance => _instance.Value;

        private static readonly object _saveLock = new object();
        private static readonly object _watcherLock = new object();
        private static FileSystemWatcher? _configWatcher;
        private static System.Timers.Timer? _debounceTimer;
        private static bool _suppressReload = false; // True while we are writing the file ourselves

        /// <summary>
        /// Raised when the monitored paths collection changes.
        /// </summary>
        public event Action? PathsChanged;

        /// <summary>
        /// Notifies subscribers that the monitored paths have changed.
        /// </summary>
        public void NotifyPathsChanged()
        {
            RebuildHashes();
            PathsChanged?.Invoke();
        }

        private HashSet<string> _whitelistedProcessesHash = new(StringComparer.OrdinalIgnoreCase);
        private HashSet<string> _excludedFoldersHash = new(StringComparer.OrdinalIgnoreCase);

        private void RebuildHashes()
        {
            _whitelistedProcessesHash = new HashSet<string>(WhitelistedProcessNames ?? new(), StringComparer.OrdinalIgnoreCase);
            _excludedFoldersHash = new HashSet<string>(ExcludedFolderNames ?? new(), StringComparer.OrdinalIgnoreCase);
        }

        public bool IsProcessWhitelisted(string name) => _whitelistedProcessesHash.Contains(name);
        public bool IsFolderExcluded(string name) => _excludedFoldersHash.Contains(name);

        private static void StartWatcher()
        {
            lock (_watcherLock)
            {
                // Double-check pattern with lock
                if (_configWatcher != null) return;
                
                var path = ConfigFile;
                var directory = Path.GetDirectoryName(path);
                if (directory == null || !Directory.Exists(directory)) return;

                _configWatcher = new FileSystemWatcher(directory, Path.GetFileName(path));
                _configWatcher.NotifyFilter = NotifyFilters.LastWrite;
                _configWatcher.Changed += (s, e) => {
                    lock (_watcherLock)
                    {
                        _debounceTimer?.Stop();
                        _debounceTimer?.Start();
                    }
                };

                _debounceTimer = new System.Timers.Timer(250);
                _debounceTimer.AutoReset = false;
                _debounceTimer.Elapsed += (s, e) => {
                    ReloadInstance();
                };

                _configWatcher.EnableRaisingEvents = true;
            }
        }

        /// <summary>
        /// Stops and disposes the configuration file watcher.
        /// </summary>
        public static void StopWatcher()
        {
            lock (_watcherLock)
            {
                if (_configWatcher != null)
                {
                    _configWatcher.EnableRaisingEvents = false;
                    _configWatcher.Dispose();
                    _configWatcher = null;
                }
                
                if (_debounceTimer != null)
                {
                    _debounceTimer.Stop();
                    _debounceTimer.Dispose();
                    _debounceTimer = null;
                }
            }
        }

        private static void ReloadInstance()
        {
            // Skip reload if we triggered the file change ourselves
            if (_suppressReload) return;

            // Acquire lock before reading to prevent race with Save()
            lock (_saveLock)
            {
                try
                {
                    // Use a non-exclusive read to avoid conflicts with external processes
                    string json;
                    using var fs = new FileStream(ConfigFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                    using var sr = new StreamReader(fs);
                    {
                        json = sr.ReadToEnd();
                    }

                    if (string.IsNullOrEmpty(json)) return;

                    var newConfig = JsonSerializer.Deserialize<ConfigurationService>(json);
                    if (newConfig != null)
                    {
                        var instance = _instance.Value;
                        instance.MonitoredPaths = newConfig.MonitoredPaths ?? new List<string>();
                        instance.WhitelistedProcessNames = newConfig.WhitelistedProcessNames ?? new List<string>();
                        instance.SensitivityLevel = newConfig.SensitivityLevel;
                        instance.RealTimeProtection = newConfig.RealTimeProtection;
                        instance.AutoQuarantine = newConfig.AutoQuarantine;
                        instance.WatchdogEnabled = newConfig.WatchdogEnabled;
                        instance.ExcludedFolderNames = newConfig.ExcludedFolderNames ?? new List<string> { "obj", "bin", ".git", ".vs", "node_modules", "vendor", ".idea" };
                        instance.LastScanTime = newConfig.LastScanTime;
                        instance.HasAutoPopulated = newConfig.HasAutoPopulated;
                        instance.NetworkIsolationEnabled = newConfig.NetworkIsolationEnabled;
                        instance.EmergencyShutdownEnabled = newConfig.EmergencyShutdownEnabled;
                        instance.BaseThreatScore = newConfig.BaseThreatScore;
                        instance.LanCircuitBreakerEnabled = newConfig.LanCircuitBreakerEnabled;
                        instance.LanSharedSecret = newConfig.LanSharedSecret;
                        instance.LanBroadcastPort = newConfig.LanBroadcastPort;

                        
                        instance.NotifyPathsChanged();
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[ConfigurationService] Error during remote reload: {ex.Message}");
                }
            }
        }

        private static string ConfigFile => Path.Combine(
            PathConfiguration.GetConfigDirectory(),
            "config.json"
        );
        private const string LegacyConfigFile = "config_legacy.json";
        
        /// <summary>
        /// Gets or sets a value indicating whether the service is in testing mode.
        /// When true, changes are not persisted to disk.
        /// </summary>
        public bool IsTestingMode { get; set; } = false;
        
        /// <summary>
        /// Gets or sets a value indicating whether the configuration has been auto-populated with user folders.
        /// </summary>
        public bool HasAutoPopulated { get; set; } = false;


        /// <summary>
        /// Gets or sets the list of directory paths to monitor for suspicious activity.
        /// </summary>
        public List<string> MonitoredPaths { get; set; } = new();
        
        /// <summary>
        /// Gets or sets the heuristic sensitivity level (1=Low, 2=Medium, 3=High, 4=Paranoid).
        /// </summary>
        public int SensitivityLevel { get; set; } = 3;
        
        /// <summary>
        /// Gets or sets a value indicating whether real-time protection is enabled.
        /// </summary>
        public bool RealTimeProtection { get; set; } = true;
        
        /// <summary>
        /// Gets or sets a value indicating whether detected threats should be automatically quarantined.
        /// </summary>
        public bool AutoQuarantine { get; set; } = true;
        
        public DateTime LastScanTime { get; set; } = DateTime.MinValue;

        /// <summary>
        /// Gets or sets the timestamp when the protection service was last stopped.
        /// Used for "Cold Scans" to detect files modified while the guard was away.
        /// </summary>
        public DateTime LastServiceStopTime { get; set; } = DateTime.MinValue;

        /// <summary>
        /// Gets or sets the total number of security scans performed since installation.
        /// Incremented by the scan logic; never estimated.
        /// </summary>
        public int TotalScansCount { get; set; } = 0;

        /// <summary>
        /// Gets or sets the list of directory names to exclude from scanning (e.g., "obj", "bin").
        /// </summary>
        public List<string> ExcludedFolderNames { get; set; } = new() { "obj", "bin", ".git", ".vs", "node_modules", "vendor", ".idea" };

        /// <summary>
        /// Gets or sets the list of process names that have been manually whitelisted by the user.
        /// </summary>
        public List<string> WhitelistedProcessNames { get; set; } = new();

        /// <summary>
        /// Gets or sets whether the Watchdog process is enabled.
        /// When enabled, the Watchdog monitors and auto-restarts the UI and Service if they stop.
        /// When disabled, the Watchdog is killed and nothing auto-restarts.
        /// </summary>
        public bool WatchdogEnabled { get; set; } = true;
        
        /// <summary>
        /// The persistent baseline risk score shown on the dashboard. 
        /// Regenerated only after 1 hour of inactivity.
        /// </summary>
        public int BaseThreatScore { get; set; } = 8;

        /// <summary>
        /// The last time the baseline threat score was updated.
        /// </summary>
        public DateTime LastScoreUpdateTime { get; set; } = DateTime.MinValue;

        /// <summary>
        /// Gets or sets a value indicating whether to automatically disconnect 
        /// from the internet when a critical threat is detected.
        /// </summary>
        public bool NetworkIsolationEnabled { get; set; } = false;

        /// <summary>
        /// Gets or sets a value indicating whether to automatically shut down 
        /// the PC when a critical threat is detected.
        /// </summary>
        public bool EmergencyShutdownEnabled { get; set; } = false;

        /// <summary>
        /// Gets or sets whether the LAN Circuit Breaker is enabled.
        /// When enabled, this node broadcasts beacons and responds to circuit break signals from peers.
        /// </summary>
        public bool LanCircuitBreakerEnabled { get; set; } = false;

        /// <summary>
        /// Shared secret for HMAC authentication of LAN broadcast messages.
        /// If empty, all LAN peers are trusted (open mode).
        /// </summary>
        public string LanSharedSecret { get; set; } = string.Empty;

        /// <summary>
        /// UDP port for LAN Circuit Breaker communication.
        /// </summary>
        public int LanBroadcastPort { get; set; } = 47700;


        /// <summary>
        /// Saves the current configuration to disk in a thread-safe manner.
        /// </summary>
        public void Save()
        {
            if (IsTestingMode) return;

            lock (_saveLock)
            {
                try
                {
                    var configPath = ConfigFile;
                    var directory = Path.GetDirectoryName(configPath);
                    
                    // Ensure directory exists
                    if (directory != null && !Directory.Exists(directory))
                    {
                        Directory.CreateDirectory(directory);
                    }
                    
                    // Suppress the FileSystemWatcher reload — we are writing the file ourselves,
                    // so we don't want ReloadInstance() to overwrite our in-memory state.
                    _suppressReload = true;
                    try
                    {
                        var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
                        File.WriteAllText(configPath, json);
                    }
                    finally
                    {
                        // Keep suppressed for 500ms to cover the watcher debounce window (250ms)
                        Task.Delay(500).ContinueWith(_ => _suppressReload = false);
                    }
                }
                catch (Exception ex)
                {
                    _suppressReload = false;
                    System.Diagnostics.Debug.WriteLine($"[ConfigurationService] Failed to save configuration: {ex.Message}");
                }
            }

            // Notify local subscribers (like Dashboard UI) immediately
            NotifyPathsChanged();
        }

        /// <summary>
        /// Loads configuration from disk or returns default configuration if file doesn't exist or is corrupted.
        /// </summary>
        /// <returns>A ConfigurationService instance with loaded or default settings.</returns>
        private static ConfigurationService Load()
        {
            var configPath = ConfigFile;
            var directory = Path.GetDirectoryName(configPath);
            if (directory != null && !Directory.Exists(directory)) Directory.CreateDirectory(directory);

            // Migration logic: If new config doesn't exist but legacy one does, move it
            if (!File.Exists(configPath) && File.Exists(LegacyConfigFile))
            {
                try
                {
                    File.Copy(LegacyConfigFile, configPath, true);
                    System.Diagnostics.Debug.WriteLine($"Migrated configuration from {LegacyConfigFile} to {configPath}");
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Failed to migrate configuration: {ex.Message}");
                }
            }

            if (File.Exists(configPath))
            {
                try
                {
                    // Use synchronous read to avoid deadlock on blocked thread-pool scheduler
                    var json = File.ReadAllText(configPath);
                    var config = JsonSerializer.Deserialize<ConfigurationService>(json);
                    
                    // Validate deserialized config
                    if (config != null)
                    {
                        // Ensure collections are not null
                        config.MonitoredPaths ??= new List<string>();
                        config.WhitelistedProcessNames ??= new List<string>();
                        config.ExcludedFolderNames ??= new List<string> { "obj", "bin", ".git", ".vs", "node_modules", "vendor", ".idea" };

                        // Clean up any WindowsApps paths that were accidentally added
                        int removed = config.MonitoredPaths.RemoveAll(p =>
                            p.Contains("WindowsApps", StringComparison.OrdinalIgnoreCase));
                        if (removed > 0)
                        {
                            System.Diagnostics.Debug.WriteLine($"[ConfigurationService] Removed {removed} WindowsApps path(s) from monitored folders.");
                            config.Save();
                        }

                        // Start watching for external changes if not in testing mode
                        if (!config.IsTestingMode)
                        {
                            StartWatcher();
                        }

                        // Robust check: Only auto-populate if we are in a User session (Interactive).
                        // The Service should NEVER auto-populate because its "Standard Folders" are different (System32).
                        bool isUserSession = Environment.UserInteractive;
                        bool hasAtLeastOneStandardFolder = config.MonitoredPaths.Any(p => ConfigurationService.IsStandardProtectedFolder(p));
                        
                        if (!config.HasAutoPopulated && !hasAtLeastOneStandardFolder && isUserSession)
                        {
                            config.PopulateDefaultFolders();
                            
                            // If we found something, mark as officially populated
                            if (config.MonitoredPaths.Any(p => ConfigurationService.IsStandardProtectedFolder(p)))
                            {
                                config.HasAutoPopulated = true;
                            }
                            
                            config.Save();
                        }

                        return config;
                    }
                }
                catch (Exception ex)
                {
                    // Log error and fall back to defaults
                    System.Diagnostics.Debug.WriteLine($"Failed to load configuration at {configPath}, using defaults: {ex.Message}");
                }
            }
            
            // Return default configuration with standard paths
            var defaultConfig = new ConfigurationService();
            defaultConfig.PopulateDefaultFolders();
            
            // Save the newly created default configuration
            defaultConfig.Save();
            defaultConfig.NotifyPathsChanged();
            
            return defaultConfig;
        }

        // Add a constructor to ensure hashes are built on first creation
        public ConfigurationService()
        {
            RebuildHashes();
        }

        /// <summary>
        /// Resets the current configuration to its factory default values.
        /// Does NOT save to disk automatically unless Save() is called.
        /// </summary>
        public void ResetToDefaults()
        {
            MonitoredPaths = new List<string>();
            PopulateDefaultFolders();
            SensitivityLevel = 3;
            RealTimeProtection = true;
            AutoQuarantine = true;
            WatchdogEnabled = true;
            LastScanTime = DateTime.MinValue;
            TotalScansCount = 0;
            ExcludedFolderNames = new List<string> { "obj", "bin", ".git", ".vs", "node_modules", "vendor", ".idea" };
            WhitelistedProcessNames = new List<string>();
            NotifyPathsChanged();
        }

        /// <summary>
        /// Populates the monitored paths with standard user folders if they exist.
        /// </summary>
        public void PopulateDefaultFolders()
        {
            try
            {
                var folders = new[]
                {
                    Environment.SpecialFolder.MyDocuments,
                    Environment.SpecialFolder.Desktop,
                    Environment.SpecialFolder.MyPictures,
                    Environment.SpecialFolder.MyMusic,
                    Environment.SpecialFolder.MyVideos,
                };

                foreach (var folder in folders)
                {
                    var path = Environment.GetFolderPath(folder);
                    if (!string.IsNullOrEmpty(path) && Directory.Exists(path))
                    {
                        var normalized = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                        if (!MonitoredPaths.Any(p => string.Equals(p, normalized, StringComparison.OrdinalIgnoreCase)))
                        {
                            MonitoredPaths.Add(normalized);
                        }
                    }
                    else
                    {
                        Console.WriteLine($"[PopulateDefaultFolders]   Skipped (Null or Not Found)");
                    }
                }

                // Downloads folder (not in SpecialFolder enum)
                var downloadPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
                if (Directory.Exists(downloadPath) && !MonitoredPaths.Any(p => string.Equals(p, downloadPath, StringComparison.OrdinalIgnoreCase)))
                    MonitoredPaths.Add(downloadPath);

                // OneDrive (Common target for ransomware)
                var oneDrivePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "OneDrive");
                if (Directory.Exists(oneDrivePath) && !MonitoredPaths.Any(p => string.Equals(p, oneDrivePath, StringComparison.OrdinalIgnoreCase)))
                    MonitoredPaths.Add(oneDrivePath);

                // Fallback: If no system folders accessible, monitor the application directory
                // (Only if we are NOT in the protected WindowsApps folder to avoid clutter)
                if (MonitoredPaths.Count == 0)
                {
                    string baseDir = AppDomain.CurrentDomain.BaseDirectory;
                    if (!baseDir.Contains("WindowsApps", StringComparison.OrdinalIgnoreCase))
                    {
                        MonitoredPaths.Add(baseDir);
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to populate default folders: {ex.Message}");
            }
        }

        /// <summary>
        /// Checks if a given path is one of the standard protected folders.
        /// </summary>
        /// <param name="path">The path to check.</param>
        /// <returns>True if the path is a standard system folder.</returns>
        public static bool IsStandardProtectedFolder(string path)
        {
            if (string.IsNullOrEmpty(path)) return false;

            var standardFolders = new List<string>();
            try
            {
                var folders = new[]
                {
                    Environment.SpecialFolder.MyDocuments,
                    Environment.SpecialFolder.Desktop,
                    Environment.SpecialFolder.MyPictures,
                    Environment.SpecialFolder.MyMusic,
                    Environment.SpecialFolder.MyVideos,
                };

                foreach (var folder in folders)
                {
                    var folderPath = Environment.GetFolderPath(folder);
                    if (!string.IsNullOrEmpty(folderPath)) standardFolders.Add(folderPath);
                }

                // Downloads
                var downloadPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
                standardFolders.Add(downloadPath);

                // OneDrive
                var oneDrivePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "OneDrive");
                standardFolders.Add(oneDrivePath);
                
                // Application base directory (portable fallback)
                standardFolders.Add(AppDomain.CurrentDomain.BaseDirectory);
            }
            catch { }

            // Normalize both sides to lowercase for comparison — saved paths may be lowercased
            // by PopulateDefaultFolders() while GetFolderPath() returns mixed case.
            string normalizedInput = path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).ToLowerInvariant();
            return standardFolders.Any(f =>
                string.Equals(
                    f.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).ToLowerInvariant(),
                    normalizedInput,
                    StringComparison.OrdinalIgnoreCase));
        }
    }
}
