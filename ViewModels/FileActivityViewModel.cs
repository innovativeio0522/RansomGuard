using CommunityToolkit.Mvvm.ComponentModel;
using RansomGuard.Models;
using RansomGuard.Services;
using System.Collections.ObjectModel;

namespace RansomGuard.ViewModels
{
    public partial class FileActivityViewModel : ViewModelBase
    {
        private readonly ISystemMonitorService _monitorService;

        public ObservableCollection<FileActivity> RecentActivities { get; } = new();

        public FileActivityViewModel()
        {
            _monitorService = new MockMonitorService();
            LoadData();
        }

        private void LoadData()
        {
            foreach (var activity in _monitorService.GetRecentFileActivities())
            {
                RecentActivities.Add(activity);
            }
        }
    }
}
