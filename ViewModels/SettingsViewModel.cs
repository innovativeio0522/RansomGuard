using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using System.Linq;
using System.ComponentModel;
using System.Windows.Threading;
using Microsoft.Win32;
using RansomGuard.Services;
using RansomGuard.Core.Services;

namespace RansomGuard.ViewModels
{
    public partial class SettingsViewModel : ViewModelBase, IDisposable
    {
        private readonly DispatcherTimer _saveDebounceTimer;
        private bool _disposed;

        [ObservableProperty]
        private ObservableCollection<string> _monitoredPaths;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(SensitivityLabel))]
        private int _sensitivityLevel;

        [ObservableProperty]
        private bool _isRealTimeProtectionEnabled;

        [ObservableProperty]
        private bool _isAutoQuarantineEnabled;

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

            // Map config to properties
            MonitoredPaths = new ObservableCollection<string>(ConfigurationService.Instance.MonitoredPaths);
            SensitivityLevel = ConfigurationService.Instance.SensitivityLevel;
            IsRealTimeProtectionEnabled = ConfigurationService.Instance.RealTimeProtection;
            IsAutoQuarantineEnabled = ConfigurationService.Instance.AutoQuarantine;

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
                e.PropertyName == nameof(IsAutoQuarantineEnabled))
            {
                SaveConfig();
            }
        }

        private void SaveConfig()
        {
            // Debounce: reset timer on each change
            _saveDebounceTimer.Stop();
            _saveDebounceTimer.Start();
        }

        private void SaveConfigImmediate()
        {
            ConfigurationService.Instance.MonitoredPaths = MonitoredPaths.ToList();
            ConfigurationService.Instance.SensitivityLevel = SensitivityLevel;
            ConfigurationService.Instance.RealTimeProtection = IsRealTimeProtectionEnabled;
            ConfigurationService.Instance.AutoQuarantine = IsAutoQuarantineEnabled;
            ConfigurationService.Instance.Save();
            
            // Notify other services (like SentinelEngine) that paths have changed
            ConfigurationService.Instance.NotifyPathsChanged();
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
                if (!MonitoredPaths.Contains(dialog.FolderName))
                {
                    MonitoredPaths.Add(dialog.FolderName);
                }
            }
        }

        [RelayCommand]
        private void RemovePath(object parameter)
        {
            if (parameter is string path && MonitoredPaths.Contains(path))
            {
                MonitoredPaths.Remove(path);
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
