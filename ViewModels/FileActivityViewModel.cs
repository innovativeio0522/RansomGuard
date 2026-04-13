using CommunityToolkit.Mvvm.ComponentModel;
using RansomGuard.Core.Models;
using RansomGuard.Core.Interfaces;
using RansomGuard.Services;
using System.Collections.ObjectModel;
using System.Windows;

namespace RansomGuard.ViewModels
{
    public partial class FileActivityViewModel : ViewModelBase
    {
        private readonly ISystemMonitorService _monitorService;

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
                if (RecentActivities.Count > 150)
                {
                    RecentActivities.RemoveAt(RecentActivities.Count - 1);
                }
            });
        }
    }
}
