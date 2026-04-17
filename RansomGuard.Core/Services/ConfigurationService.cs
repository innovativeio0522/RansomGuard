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

        /// <summary>
        /// Raised when the monitored paths collection changes.
        /// </summary>
        public event Action? PathsChanged;

        /// <summary>
        /// Notifies subscribers that the monitored paths have changed.
        /// </summary>
        public void NotifyPathsChanged() => PathsChanged?.Invoke();

        private static string ConfigFile => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "RansomGuard",
            "config.json"
        );
        private const string LegacyConfigFile = "config_legacy.json";


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

                        Console.WriteLine($"[ConfigurationService] MonitoredPaths count before auto-populate: {config.MonitoredPaths.Count}");

                        // If no paths monitored, populate with standard folders
                        if (config.MonitoredPaths.Count == 0)
                        {
                            Console.WriteLine("[ConfigurationService] Auto-populating default folders...");
                            config.PopulateDefaultFolders();
                            Console.WriteLine($"[ConfigurationService] Auto-populated {config.MonitoredPaths.Count} folders");
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
                        MonitoredPaths.Add(path);
                }

                // Downloads folder (not in SpecialFolder enum)
                var downloadPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
                if (Directory.Exists(downloadPath))
                    MonitoredPaths.Add(downloadPath);

                // OneDrive (Common target for ransomware)
                var oneDrivePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "OneDrive");
                if (Directory.Exists(oneDrivePath))
                    MonitoredPaths.Add(oneDrivePath);

                // Fallback: If no system folders accessible, monitor the project directory/CWD
                if (MonitoredPaths.Count == 0)
                {
                    MonitoredPaths.Add(@"F:\Github Projects\RansomGuard");
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
                
                // Project root (as a fallback)
                standardFolders.Add(@"F:\Github Projects\RansomGuard");
            }
            catch { }

            return standardFolders.Any(f => string.Equals(f, path, StringComparison.OrdinalIgnoreCase));
        }
    }
}
