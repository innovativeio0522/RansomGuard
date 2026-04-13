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

        private const string ConfigFile = "config.json";

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
        /// Saves the current configuration to disk in a thread-safe manner.
        /// </summary>
        public void Save()
        {
            lock (_saveLock)
            {
                try
                {
                    var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
                    File.WriteAllTextAsync(ConfigFile, json).GetAwaiter().GetResult();
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
            if (File.Exists(ConfigFile))
            {
                try
                {
                    // Use async file I/O for better performance
                    var json = File.ReadAllTextAsync(ConfigFile).GetAwaiter().GetResult();
                    var config = JsonSerializer.Deserialize<ConfigurationService>(json);
                    
                    // Validate deserialized config
                    if (config != null)
                    {
                        // Ensure collections are not null
                        config.MonitoredPaths ??= new List<string>();
                        return config;
                    }
                }
                catch (Exception ex)
                {
                    // Log error and fall back to defaults
                    System.Diagnostics.Debug.WriteLine($"Failed to load configuration, using defaults: {ex.Message}");
                }
            }
            
            // Return default configuration
            return new ConfigurationService();
        }
    }
}
