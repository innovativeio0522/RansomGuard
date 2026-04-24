using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using System.Linq;
using System.ComponentModel;
using System.Windows.Threading;
using System.IO;
using System.Diagnostics;
using Microsoft.Win32;
using RansomGuard.Services;
using RansomGuard.Core.Services;

namespace RansomGuard.ViewModels
{
    public partial class MonitoredPathItem : ObservableObject
    {
        [ObservableProperty] private string _path;
        [ObservableProperty] private bool _isStandard;

        public MonitoredPathItem(string path)
        {
            _path = path;
            _isStandard = ConfigurationService.IsStandardProtectedFolder(path);
        }
    }

    public partial class SettingsViewModel : ViewModelBase, IDisposable
    {
        private readonly DispatcherTimer _saveDebounceTimer;
        private bool _disposed;
        private bool _isInitialized;

        [ObservableProperty]
        private ObservableCollection<MonitoredPathItem> _monitoredPaths;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(SensitivityLabel))]
        private int _sensitivityLevel;

        [ObservableProperty]
        private bool _isRealTimeProtectionEnabled;

        [ObservableProperty]
        private bool _isAutoQuarantineEnabled;

        [ObservableProperty]
        private bool _isWatchdogEnabled;

        [ObservableProperty]
        private bool _isNetworkIsolationEnabled;

        [ObservableProperty]
        private bool _isEmergencyShutdownEnabled;

        public string SensitivityLabel => SensitivityLevel switch
        {
            1 => "LOW",
            2 => "MEDIUM",
            3 => "HIGH",
            4 => "PARANOID",
            _ => "UNKNOWN"
        };

        public SettingsViewModel()
        {
            // Initialize debounce timer (500ms delay)
            _saveDebounceTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(500)
            };
            _saveDebounceTimer.Tick += (s, e) =>
            {
                _saveDebounceTimer.Stop();
                SaveConfigImmediate();
            };

            _monitoredPaths = new ObservableCollection<MonitoredPathItem>();
            LoadMonitoredPaths();
            
            SensitivityLevel = ConfigurationService.Instance.SensitivityLevel;
            IsRealTimeProtectionEnabled = ConfigurationService.Instance.RealTimeProtection;
            IsAutoQuarantineEnabled = ConfigurationService.Instance.AutoQuarantine;
            IsWatchdogEnabled = ConfigurationService.Instance.WatchdogEnabled;
            IsNetworkIsolationEnabled = ConfigurationService.Instance.NetworkIsolationEnabled;
            IsEmergencyShutdownEnabled = ConfigurationService.Instance.EmergencyShutdownEnabled;

            // Handle collection changes with debouncing
            _monitoredPaths.CollectionChanged += (s, e) => SaveConfig();
        }

        partial void OnSensitivityLevelChanged(int value)
        {
            OnPropertyChanged(nameof(SensitivityLabel));
            SaveConfig();
        }

        protected override void OnPropertyChanged(PropertyChangedEventArgs e)
        {
            base.OnPropertyChanged(e);
            
            // Auto-save on other property changes
            if (e.PropertyName == nameof(IsRealTimeProtectionEnabled) || 
                e.PropertyName == nameof(IsAutoQuarantineEnabled) ||
                e.PropertyName == nameof(IsWatchdogEnabled) ||
                e.PropertyName == nameof(IsNetworkIsolationEnabled) ||
                e.PropertyName == nameof(IsEmergencyShutdownEnabled))
            {
                SaveConfig();
            }
        }

        partial void OnIsWatchdogEnabledChanged(bool value)
        {
            // Update the configuration instance immediately so the Watchdog sees the correct state on startup
            ConfigurationService.Instance.WatchdogEnabled = value;
            ConfigurationService.Instance.Save();

            if (value)
            {
                // Spawn Watchdog if not already running
                WatchdogManager.EnsureWatchdogRunning();
            }
            else
            {
                // Kill the Watchdog process
                WatchdogManager.KillWatchdog();
            }
        }

        private void LoadMonitoredPaths()
        {
            _isInitialized = false;
            var config = ConfigurationService.Instance;

            // Robust check: If we don't have ANY standard folders (Documents, Desktop, etc.), 
            // we should try to discover them, regardless of what fallbacks are present.
            bool hasAtLeastOneStandardFolder = config.MonitoredPaths.Any(p => ConfigurationService.IsStandardProtectedFolder(p));
            
            Console.WriteLine($"[ConfigurationService] hasAtLeastOneStandardFolder: {hasAtLeastOneStandardFolder}, HasAutoPopulated: {config.HasAutoPopulated}");
            
            if (!config.HasAutoPopulated && !hasAtLeastOneStandardFolder)
            {
                Console.WriteLine("[ConfigurationService] No standard folders found. Re-triggering discovery...");
                config.PopulateDefaultFolders();
                
                // If we found something, mark as officially populated
                if (config.MonitoredPaths.Any(p => ConfigurationService.IsStandardProtectedFolder(p)))
                {
                    config.HasAutoPopulated = true;
                    
                    // Cleanup: Remove the fallback App folder if we now have real folders.
                    var baseDir = AppDomain.CurrentDomain.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                    var fallbackPath = config.MonitoredPaths.FirstOrDefault(p => 
                        p.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Equals(baseDir, StringComparison.OrdinalIgnoreCase));
                    
                    if (fallbackPath != null && config.MonitoredPaths.Count > 1)
                    {
                        config.MonitoredPaths.Remove(fallbackPath);
                    }
                }
                
                config.Save();
            }

            MonitoredPaths.Clear();
            foreach (var path in config.MonitoredPaths)
            {
                MonitoredPaths.Add(new MonitoredPathItem(path));
            }
            _isInitialized = true;
        }

        private void SaveConfig()
        {
            if (!_isInitialized) return;

            // Debounce: reset timer on each change
            _saveDebounceTimer.Stop();
            _saveDebounceTimer.Start();
        }

        private void SaveConfigImmediate()
        {
            ConfigurationService.Instance.MonitoredPaths = MonitoredPaths.Select(m => m.Path).ToList();
            ConfigurationService.Instance.SensitivityLevel = SensitivityLevel;
            ConfigurationService.Instance.RealTimeProtection = IsRealTimeProtectionEnabled;
            ConfigurationService.Instance.AutoQuarantine = IsAutoQuarantineEnabled;
            ConfigurationService.Instance.WatchdogEnabled = IsWatchdogEnabled;
            ConfigurationService.Instance.NetworkIsolationEnabled = IsNetworkIsolationEnabled;
            ConfigurationService.Instance.EmergencyShutdownEnabled = IsEmergencyShutdownEnabled;
            ConfigurationService.Instance.Save();
            
            // Notify other services (like SentinelEngine) that paths have changed
            ConfigurationService.Instance.NotifyPathsChanged();
        }

        [RelayCommand]
        private void ShowLicenseInfo()
        {
            System.Windows.MessageBox.Show(
                "Product: RansomGuard Business Edition\n" +
                "License: Active (Node ID: TS-8849-PX)\n" +
                "Expiration: 12-NOV-2025\n\n" +
                "This node is registered to 'Enterprise Security Cluster 01'.",
                "License Information",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Information);
        }

        [RelayCommand]
        private void OpenHelp(string url)
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = url ?? "https://github.com/innovativeio0522/RansomGuard",
                    UseShellExecute = true
                });
            }
            catch { }
        }

        [RelayCommand]
        private void AddPath()
        {
            var dialog = new OpenFolderDialog
            {
                Title = "Select Directory for Protection",
                InitialDirectory = System.Environment.GetFolderPath(System.Environment.SpecialFolder.MyDocuments)
            };

            if (dialog.ShowDialog() == true)
            {
                if (!MonitoredPaths.Any(m => string.Equals(m.Path, dialog.FolderName, StringComparison.OrdinalIgnoreCase)))
                {
                    MonitoredPaths.Add(new MonitoredPathItem(dialog.FolderName));
                }
            }
        }

        [RelayCommand]
        private void RemovePath(object parameter)
        {
            if (parameter is MonitoredPathItem item)
            {
                // Only allow removal of non-standard folders
                if (!item.IsStandard)
                {
                    MonitoredPaths.Remove(item);
                }
            }
        }

        [RelayCommand]
        private void InstallService()
        {
            try
            {
                string servicePath = System.IO.Path.Combine(System.AppDomain.CurrentDomain.BaseDirectory, "RansomGuard.Service.exe");
                ServiceManager.InstallService(servicePath);
                System.Windows.MessageBox.Show("RansomGuard Protection Service has been successfully installed and started.", "Service Installation", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
            }
            catch (System.Exception ex)
            {
                System.Windows.MessageBox.Show($"Failed to install the background service: {ex.Message}", "Service Error", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            // Stop timer and save any pending changes immediately
            if (_saveDebounceTimer != null)
            {
                _saveDebounceTimer.Stop();
                
                // Flush any pending save
                SaveConfigImmediate();
            }
        }
    }
}
