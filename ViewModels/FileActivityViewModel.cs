using CommunityToolkit.Mvvm.ComponentModel;
using RansomGuard.Core.Models;
using RansomGuard.Core.Interfaces;
using RansomGuard.Services;
using System.Collections.ObjectModel;
using System.Windows;

namespace RansomGuard.ViewModels
{
    public partial class FileActivityViewModel : ViewModelBase, IDisposable
    {
        private const int MaxRecentActivities = 150;
        
        private readonly ISystemMonitorService _monitorService;
        private bool _disposed;

        public ObservableCollection<FileActivity> RecentActivities { get; } = new();

        public FileActivityViewModel(ISystemMonitorService monitorService)
        {
            _monitorService = monitorService;
            
            // Initial load
            foreach (var activity in _monitorService.GetRecentFileActivities())
            {
                RecentActivities.Add(activity);
            }

            // Subscribe to live updates
            _monitorService.FileActivityDetected += OnFileActivityDetected;
        }

        private void OnFileActivityDetected(FileActivity activity)
        {
            // Ensure thread-safe update to the collection
            Application.Current.Dispatcher.Invoke(() =>
            {
                // Add to top of list
                RecentActivities.Insert(0, activity);

                // Keep buffer manageable
                if (RecentActivities.Count > MaxRecentActivities)
                {
                    RecentActivities.RemoveAt(RecentActivities.Count - 1);
                }
            });
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            // Unsubscribe from events
            if (_monitorService != null)
            {
                _monitorService.FileActivityDetected -= OnFileActivityDetected;
            }
        }
    }
}
