using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using System.Linq;
using System.ComponentModel;
using System.Windows.Threading;
using System.IO;
using System.Diagnostics;
using System.Windows;
using Microsoft.Win32;
using RansomGuard.Services;
using RansomGuard.Core.Services;
using RansomGuard.Core.Interfaces;
using RansomGuard.Core.Configuration;
using RansomGuard.Core.IPC;

namespace RansomGuard.ViewModels
{
    public partial class MonitoredPathItem : ObservableObject
    {
        [ObservableProperty] private string _path;
        [ObservableProperty] private bool _isStandard;
        public string CategoryLabel => IsStandard ? "Standard" : "Custom";

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
        private readonly ISystemMonitorService? _monitorService;

        // Named handlers for proper unsubscription
        private Action<bool>? _connectionStatusChangedHandler;
        private System.Collections.Specialized.NotifyCollectionChangedEventHandler? _pathsCollectionChangedHandler;
        private EventHandler? _saveDebounceTimerHandler;

        [ObservableProperty]
        private ObservableCollection<MonitoredPathItem> _monitoredPaths;

        [ObservableProperty]
        private bool _isServiceOperationInProgress;

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

        [ObservableProperty]
        private bool _isServiceInstalled;

        [ObservableProperty]
        private bool _isLanCircuitBreakerEnabled;

        [ObservableProperty]
        private string _lanSharedSecret = string.Empty;

        [ObservableProperty]
        private ObservableCollection<LanPeer> _lanPeers = new();

        private Action<LanPeerListUpdate>? _lanPeerListUpdatedHandler;

        public IEnumerable<MonitoredPathItem> StandardProtectedPaths => MonitoredPaths.Where(path => path.IsStandard);
        public IEnumerable<MonitoredPathItem> UserAddedProtectedPaths => MonitoredPaths.Where(path => !path.IsStandard);
        public bool HasUserAddedProtectedPaths => MonitoredPaths.Any(path => !path.IsStandard);

        public string SensitivityLabel => SensitivityLevel switch
        {
            1 => "LOW",
            2 => "MEDIUM",
            3 => "HIGH",
            4 => "PARANOID",
            _ => "UNKNOWN"
        };

        public SettingsViewModel(ISystemMonitorService? monitorService = null)
        {
            _monitorService = monitorService;

            _saveDebounceTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(AppConstants.Timers.SettingsDebounceMs)
            };
            // Initialize debounce timer — use named handler for proper unsubscription
            _saveDebounceTimerHandler = (s, e) =>
            {
                if (_disposed) return;
                _saveDebounceTimer.Stop();
                SaveConfigImmediate();
            };
            _saveDebounceTimer.Tick += _saveDebounceTimerHandler;

            _monitoredPaths = new ObservableCollection<MonitoredPathItem>();
            LoadMonitoredPaths();
            
            SensitivityLevel = ConfigurationService.Instance.SensitivityLevel;
            IsRealTimeProtectionEnabled = ConfigurationService.Instance.RealTimeProtection;
            IsAutoQuarantineEnabled = ConfigurationService.Instance.AutoQuarantine;
            IsWatchdogEnabled = ConfigurationService.Instance.WatchdogEnabled;
            IsNetworkIsolationEnabled = ConfigurationService.Instance.NetworkIsolationEnabled;
            IsEmergencyShutdownEnabled = ConfigurationService.Instance.EmergencyShutdownEnabled;
            IsLanCircuitBreakerEnabled = ConfigurationService.Instance.LanCircuitBreakerEnabled;
            LanSharedSecret = ConfigurationService.Instance.LanSharedSecret;
            IsServiceInstalled = ServiceManager.IsServiceInstalled();

            // Subscribe to service connection changes using named handler for proper unsubscription
            if (_monitorService != null)
            {
                _connectionStatusChangedHandler = (isConnected) =>
                {
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        if (!isConnected)
                            IsRealTimeProtectionEnabled = false;
                        else
                            IsRealTimeProtectionEnabled = ConfigurationService.Instance.RealTimeProtection;
                    });
                };
                _monitorService.ConnectionStatusChanged += _connectionStatusChangedHandler;
                
                _lanPeerListUpdatedHandler = OnLanPeerListUpdated;
                _monitorService.LanPeerListUpdated += _lanPeerListUpdatedHandler;
            }

            // Handle collection changes with debouncing — named handler for proper unsubscription
            _pathsCollectionChangedHandler = (s, e) =>
            {
                OnPropertyChanged(nameof(StandardProtectedPaths));
                OnPropertyChanged(nameof(UserAddedProtectedPaths));
                OnPropertyChanged(nameof(HasUserAddedProtectedPaths));
                SaveConfig();
            };
            _monitoredPaths.CollectionChanged += _pathsCollectionChangedHandler;
        }

        partial void OnSensitivityLevelChanged(int value)
        {
            OnPropertyChanged(nameof(SensitivityLabel));
            SaveConfig();
        }

        partial void OnIsLanCircuitBreakerEnabledChanged(bool value)
        {
            SaveConfig();
        }
        partial void OnLanSharedSecretChanged(string value) => SaveConfig();

        protected override void OnPropertyChanged(PropertyChangedEventArgs e)
        {
            base.OnPropertyChanged(e);
            
            // Auto-save on other property changes
            // Note: IsWatchdogEnabled is handled specially in OnIsWatchdogEnabledChanged and doesn't need debouncing
            if (e.PropertyName == nameof(IsRealTimeProtectionEnabled) || 
                e.PropertyName == nameof(IsAutoQuarantineEnabled) ||
                e.PropertyName == nameof(IsNetworkIsolationEnabled) ||
                e.PropertyName == nameof(IsEmergencyShutdownEnabled))
            {
                SaveConfig();
            }
        }

        partial void OnIsWatchdogEnabledChanged(bool value)
        {
            // Stop any pending debounced save to prevent conflicts
            _saveDebounceTimer?.Stop();
            
            // Update the configuration instance immediately so the Watchdog sees the correct state on startup
            ConfigurationService.Instance.WatchdogEnabled = value;
            ConfigurationService.Instance.Save();

            if (value)
            {
                // Spawn Watchdog if not already running
                WatchdogManager.EnsureProtectionEngaged();
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
            var configPaths = config.MonitoredPaths ?? new List<string>();

            bool hasAtLeastOneStandardFolder = configPaths.Any(p => ConfigurationService.IsStandardProtectedFolder(p));
            
            if (!config.HasAutoPopulated && !hasAtLeastOneStandardFolder)
            {
                config.PopulateDefaultFolders();
                configPaths = config.MonitoredPaths ?? new List<string>();
                
                if (configPaths.Any(p => ConfigurationService.IsStandardProtectedFolder(p)))
                {
                    config.HasAutoPopulated = true;
                    
                    var baseDir = AppDomain.CurrentDomain.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                    var fallbackPath = configPaths.FirstOrDefault(p => 
                        p.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                         .Equals(baseDir, StringComparison.OrdinalIgnoreCase));
                    
                    if (fallbackPath != null && configPaths.Count > 1)
                        config.MonitoredPaths?.Remove(fallbackPath);
                }
                
                config.Save();
            }

            MonitoredPaths.Clear();
            foreach (var path in configPaths)
                MonitoredPaths.Add(new MonitoredPathItem(path));

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
            ConfigurationService.Instance.LanCircuitBreakerEnabled = IsLanCircuitBreakerEnabled;
            ConfigurationService.Instance.LanSharedSecret = LanSharedSecret;
            ConfigurationService.Instance.Save();
            
            // Notify other services (like SentinelEngine) that paths have changed
            ConfigurationService.Instance.NotifyPathsChanged();

            // Explicitly notify the background service via IPC to reload configuration and update watchers/circuit-breaker
            _monitorService?.InitializeWatchers();
        }

        [RelayCommand]
        private void ShowLicenseInfo()
        {
            System.Windows.MessageBox.Show(
                "Product: RG Core Essentials\n" +
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
                    FileName = url ?? "https://github.com/innovativeio0522/RGCoreEssentials",
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[SettingsViewModel] Failed to open URL: {ex.Message}");
            }
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
        private async Task InstallService()
        {
            if (IsServiceOperationInProgress) return;
            
            try
            {
                IsServiceOperationInProgress = true;

                // Check if service is already installed
                bool isInstalled = await Task.Run(() => ServiceManager.IsServiceInstalled());
                if (isInstalled)
                {
                    System.Windows.MessageBox.Show(
                        "The RansomGuard Protection Service is already installed and running.\n\nYour system is protected!",
                        "Service Already Running",
                        System.Windows.MessageBoxButton.OK,
                        System.Windows.MessageBoxImage.Information);
                    IsServiceInstalled = true;
                    return;
                }
                
                // Look for the service executable in multiple locations
                string baseDir = System.AppDomain.CurrentDomain.BaseDirectory;
                string servicePath = System.IO.Path.Combine(baseDir, "RGService.exe");
                
                // If not found in base directory, check Service subfolder (MSIX package layout)
                if (!System.IO.File.Exists(servicePath))
                {
                    string serviceSubfolderPath = System.IO.Path.Combine(baseDir, "..", "Service", "RGService.exe");
                    serviceSubfolderPath = System.IO.Path.GetFullPath(serviceSubfolderPath);
                    if (System.IO.File.Exists(serviceSubfolderPath))
                    {
                        servicePath = serviceSubfolderPath;
                    }
                    else
                    {
                        // Fallback to publish folder (used by build script)
                        string publishPath = System.IO.Path.Combine(baseDir, "..", "..", "..", "RansomGuard.Service", "publish", "RGService.exe");
                        publishPath = System.IO.Path.GetFullPath(publishPath);
                        if (System.IO.File.Exists(publishPath))
                        {
                            servicePath = publishPath;
                        }
                        else
                        {
                            throw new System.IO.FileNotFoundException($"Service executable not found. Looked in:\n- {servicePath}\n- {serviceSubfolderPath}\n- {publishPath}");
                        }
                    }
                }
                
                await Task.Run(() => ServiceManager.InstallService(servicePath));
                IsServiceInstalled = true; // Update UI
                System.Windows.MessageBox.Show("RansomGuard Protection Service has been successfully installed and started.", "Service Installation", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
            }
            catch (System.Exception ex)
            {
                System.Windows.MessageBox.Show($"Failed to install the background service: {ex.Message}", "Service Error", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
            }
            finally
            {
                IsServiceOperationInProgress = false;
            }
        }

        [RelayCommand]
        private async Task UninstallService()
        {
            if (IsServiceOperationInProgress) return;

            try
            {
                IsServiceOperationInProgress = true;

                // Check if service is installed
                bool isInstalled = await Task.Run(() => ServiceManager.IsServiceInstalled());
                if (!isInstalled)
                {
                    System.Windows.MessageBox.Show(
                        "The RansomGuard Protection Service is not installed.",
                        "Service Not Found",
                        System.Windows.MessageBoxButton.OK,
                        System.Windows.MessageBoxImage.Information);
                    return;
                }
                
                // Confirm with user
                var result = System.Windows.MessageBox.Show(
                    "Are you sure you want to uninstall the RansomGuard Protection Service?\n\nThis will stop all background protection until you reinstall it.",
                    "Confirm Uninstall",
                    System.Windows.MessageBoxButton.YesNo,
                    System.Windows.MessageBoxImage.Warning);
                
                if (result != System.Windows.MessageBoxResult.Yes)
                {
                    return;
                }
                
                await Task.Run(() => ServiceManager.UninstallService());
                IsServiceInstalled = false; // Update UI
                System.Windows.MessageBox.Show("RansomGuard Protection Service has been successfully uninstalled.", "Service Uninstalled", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
            }
            catch (System.Exception ex)
            {
                System.Windows.MessageBox.Show($"Failed to uninstall the background service: {ex.Message}", "Service Error", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
            }
            finally
            {
                IsServiceOperationInProgress = false;
            }
        }

        private void OnLanPeerListUpdated(LanPeerListUpdate update)
        {
            if (_disposed) return;
            Application.Current.Dispatcher.Invoke(() =>
            {
                LanPeers.Clear();
                foreach (var peer in update.Peers)
                {
                    LanPeers.Add(peer);
                }
            });
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            // Stop timer and save any pending changes immediately
            if (_saveDebounceTimer != null)
            {
                _saveDebounceTimer.Stop();
                if (_saveDebounceTimerHandler != null)
                {
                    _saveDebounceTimer.Tick -= _saveDebounceTimerHandler;
                    _saveDebounceTimerHandler = null;
                }
                
                // Flush any pending save
                SaveConfigImmediate();
            }

            // Unsubscribe from events
            if (_monitorService != null && _connectionStatusChangedHandler != null)
            {
                _monitorService.ConnectionStatusChanged -= _connectionStatusChangedHandler;
                _connectionStatusChangedHandler = null;
            }

            if (_monitorService != null && _lanPeerListUpdatedHandler != null)
            {
                _monitorService.LanPeerListUpdated -= _lanPeerListUpdatedHandler;
                _lanPeerListUpdatedHandler = null;
            }

            if (MonitoredPaths != null && _pathsCollectionChangedHandler != null)
            {
                MonitoredPaths.CollectionChanged -= _pathsCollectionChangedHandler;
                _pathsCollectionChangedHandler = null;
            }
        }
    }
}
