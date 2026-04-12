using CommunityToolkit.Mvvm.ComponentModel;
using RansomGuard.Models;
using RansomGuard.Services;
using System.Collections.ObjectModel;
using System.Linq;

namespace RansomGuard.ViewModels
{
    public partial class DashboardViewModel : ViewModelBase
    {
        private readonly ISystemMonitorService _monitorService;

        [ObservableProperty]
        private string _filesMonitoredCount = "0";

        [ObservableProperty]
        private int _threatsBlockedCount = 0;

        [ObservableProperty]
        private int _activeProcessesCount = 0;

        [ObservableProperty]
        private double _threatRiskScore = 12;

        public ObservableCollection<FileActivity> RecentActivities { get; } = new();
        public ObservableCollection<Threat> ActiveAlerts { get; } = new();

        public DashboardViewModel()
        {
            // For now, use the mock service
            _monitorService = new MockMonitorService();
            LoadData();
        }

        private void LoadData()
        {
            FilesMonitoredCount = _monitorService.GetMonitoredFilesCount().ToString("N0");
            
            var threats = _monitorService.GetRecentThreats().ToList();
            ThreatsBlockedCount = threats.Count;
            foreach (var threat in threats) ActiveAlerts.Add(threat);

            var activities = _monitorService.GetRecentFileActivities();
            foreach (var activity in activities) RecentActivities.Add(activity);

            ActiveProcessesCount = _monitorService.GetActiveProcesses().Count();
        }
    }
}
