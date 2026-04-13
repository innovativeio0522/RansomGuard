using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RansomGuard.Core.Models;
using RansomGuard.Core.Interfaces;
using RansomGuard.Core.Helpers;
using RansomGuard.Services;
using System.Collections.ObjectModel;
using System.Linq;
using System.IO;
using System.Windows.Threading;

namespace RansomGuard.ViewModels
{
    public partial class QuarantineViewModel : ViewModelBase, IDisposable
    {
        private readonly ISystemMonitorService _monitorService;
        private readonly DispatcherTimer _refreshTimer;
        private bool _disposed;

        public ObservableCollection<Threat> QuarantinedItems { get; } = new();

        [ObservableProperty]
        private int _totalItems;

        [ObservableProperty]
        private double _storageUsedMb;

        public QuarantineViewModel(ISystemMonitorService monitorService)
        {
            _monitorService = monitorService;
            LoadData();

            // Auto-refresh quarantine data every 5 seconds
            _refreshTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(5)
            };
            _refreshTimer.Tick += (s, e) => LoadData();
            _refreshTimer.Start();
        }

        private void LoadData()
        {
            var telemetry = _monitorService.GetTelemetry();
            TotalItems = telemetry.QuarantinedFilesCount;
            StorageUsedMb = _monitorService.GetQuarantineStorageUsage();

            var threats = _monitorService.GetRecentThreats().ToList();
            QuarantinedItems.Clear();
            foreach (var threat in threats)
            {
                QuarantinedItems.Add(threat);
            }
        }

        [RelayCommand]
        private async Task RestoreFile(Threat? threat)
        {
            if (threat == null) return;

            await Task.Run(() =>
            {
                try
                {
                    // Implement actual restore from quarantine
                    string quarantinePath = Path.Combine(
                        PathConfiguration.QuarantinePath,
                        Path.GetFileName(threat.Path) + ".quarantine"
                    );
                    
                    if (File.Exists(quarantinePath))
                    {
                        // Ensure destination directory exists
                        string? destDir = Path.GetDirectoryName(threat.Path);
                        if (!string.IsNullOrEmpty(destDir))
                        {
                            Directory.CreateDirectory(destDir);
                        }
                        
                        // Restore file to original location
                        File.Move(quarantinePath, threat.Path, overwrite: false);
                        
                        System.Diagnostics.Debug.WriteLine($"Restored file: {threat.Path}");
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Failed to restore file: {ex.Message}");
                }
            });
            
            QuarantinedItems.Remove(threat);
            LoadData();
        }

        [RelayCommand]
        private async Task DeleteFile(Threat? threat)
        {
            if (threat == null) return;

            await Task.Run(() =>
            {
                try
                {
                    // Implement actual permanent delete from quarantine
                    string quarantinePath = Path.Combine(
                        PathConfiguration.QuarantinePath,
                        Path.GetFileName(threat.Path) + ".quarantine"
                    );
                    
                    if (File.Exists(quarantinePath))
                    {
                        File.Delete(quarantinePath);
                        System.Diagnostics.Debug.WriteLine($"Permanently deleted: {quarantinePath}");
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Failed to delete file: {ex.Message}");
                }
            });
            
            QuarantinedItems.Remove(threat);
            LoadData();
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            
            // Stop and dispose refresh timer
            if (_refreshTimer != null)
            {
                _refreshTimer.Stop();
            }
        }
    }
}
