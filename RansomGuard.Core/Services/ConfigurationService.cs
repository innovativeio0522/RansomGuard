using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

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
        private static FileSystemWatcher? _configWatcher;
        private static System.Timers.Timer? _debounceTimer;

        /// <summary>
        /// Raised when the monitored paths collection changes.
        /// </summary>
        public event Action? PathsChanged;

        /// <summary>
        /// Notifies subscribers that the monitored paths have changed.
        /// </summary>
        public void NotifyPathsChanged() => PathsChanged?.Invoke();

        private static void StartWatcher()
        {
            if (_configWatcher != null) return;
            
            var path = ConfigFile;
            var directory = Path.GetDirectoryName(path);
            if (directory == null || !Directory.Exists(directory)) return;

            _configWatcher = new FileSystemWatcher(directory, Path.GetFileName(path));
            _configWatcher.NotifyFilter = NotifyFilters.LastWrite;
            _configWatcher.Changed += (s, e) => {
                _debounceTimer?.Stop();
                _debounceTimer?.Start();
            };

            _debounceTimer = new System.Timers.Timer(250);
            _debounceTimer.AutoReset = false;
            _debounceTimer.Elapsed += (s, e) => {
                ReloadInstance();
            };

            _configWatcher.EnableRaisingEvents = true;
        }

        private static void ReloadInstance()
        {
            try
            {
                // Use a non-exclusive read to avoid conflicts with saving processes
                string json;
                using (var fs = new FileStream(ConfigFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                using (var sr = new StreamReader(fs))
                {
                    json = sr.ReadToEnd();
                }

                if (string.IsNullOrEmpty(json)) return;

                var newConfig = JsonSerializer.Deserialize<ConfigurationService>(json);
                if (newConfig != null)
                {
                    lock (_saveLock)
                    {
                        var instance = _instance.Value;
                        instance.MonitoredPaths = newConfig.MonitoredPaths ?? new List<string>();
                        instance.WhitelistedProcessNames = newConfig.WhitelistedProcessNames ?? new List<string>();
                        instance.SensitivityLevel = newConfig.SensitivityLevel;
                        instance.RealTimeProtection = newConfig.RealTimeProtection;
                        instance.AutoQuarantine = newConfig.AutoQuarantine;
                        instance.LastScanTime = newConfig.LastScanTime;
                        instance.HasAutoPopulated = newConfig.HasAutoPopulated;
                        
                        instance.NotifyPathsChanged();
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ConfigurationService] Error during remote reload: {ex.Message}");
            }
        }

        private static string ConfigFile => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "RansomGuard",
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
        
        /// <summary>
        /// Gets or sets the timestamp of the last completed security scan.
        /// </summary>
        public DateTime LastScanTime { get; set; } = DateTime.MinValue;

        /// <summary>
        /// Gets or sets the total number of security scans performed since installation.
        /// Incremented by the scan logic; never estimated.
        /// </summary>
        public int TotalScansCount { get; set; } = 0;

        /// <summary>
        /// Gets or sets the list of process names that have been manually whitelisted by the user.
        /// </summary>
        public List<string> WhitelistedProcessNames { get; set; } = new();

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
                    var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
                    File.WriteAllText(ConfigFile, json);
                }
                catch (Exception ex)
                {
                    // Log error but don't crash the application
                    System.Diagnostics.Debug.WriteLine($"Failed to save configuration: {ex.Message}");
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
            Console.WriteLine($"[ConfigurationService] Loading config from: {configPath}");
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
            Console.WriteLine("[ConfigurationService] Config file not found, creating default...");
            var defaultConfig = new ConfigurationService();
            defaultConfig.PopulateDefaultFolders();
            
            // Save the newly created default configuration
            defaultConfig.Save();
            defaultConfig.NotifyPathsChanged();
            
            return defaultConfig;
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
            LastScanTime = DateTime.MinValue;
            TotalScansCount = 0;
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
                        var normalized = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).ToLowerInvariant();
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
                if (MonitoredPaths.Count == 0)
                {
                    MonitoredPaths.Add(AppDomain.CurrentDomain.BaseDirectory);
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

            return standardFolders.Any(f => string.Equals(f, path, StringComparison.OrdinalIgnoreCase));
        }
    }
}
