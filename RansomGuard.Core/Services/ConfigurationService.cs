using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace RansomGuard.Core.Services
{
    public class ConfigurationService
    {
        private static ConfigurationService? _instance;
        public static ConfigurationService Instance => _instance ??= Load();

        public event Action? PathsChanged;

        public void NotifyPathsChanged() => PathsChanged?.Invoke();

        private const string ConfigFile = "config.json";

        public List<string> MonitoredPaths { get; set; } = new();
        public int SensitivityLevel { get; set; } = 3;
        public bool RealTimeProtection { get; set; } = true;
        public bool AutoQuarantine { get; set; } = true;

        public void Save()
        {
            var json = JsonSerializer.Serialize(this);
            File.WriteAllText(ConfigFile, json);
        }

        public static ConfigurationService Load()
        {
            if (File.Exists(ConfigFile))
            {
                var json = File.ReadAllText(ConfigFile);
                return JsonSerializer.Deserialize<ConfigurationService>(json) ?? new ConfigurationService();
            }
            return new ConfigurationService();
        }
    }
}
