using CommunityToolkit.Mvvm.ComponentModel;
using RansomGuard.Core.Models;
using RansomGuard.Core.Interfaces;
using RansomGuard.Services;
using System.Collections.ObjectModel;
using System.Linq;

namespace RansomGuard.ViewModels
{
    public partial class QuarantineViewModel : ViewModelBase
    {
        private readonly ISystemMonitorService _monitorService;

        public ObservableCollection<Threat> QuarantinedItems { get; } = new();

        [ObservableProperty]
        private int _totalItems;

        [ObservableProperty]
        private double _storageUsedMb = 256.45;

        public QuarantineViewModel(ISystemMonitorService monitorService)
        {
            _monitorService = monitorService;
            LoadData();
        }

        private void LoadData()
        {
            var telemetry = _monitorService.GetTelemetry();
            TotalItems = telemetry.QuarantinedFilesCount;
            StorageUsedMb = telemetry.QuarantineStorageMb;

            var threats = _monitorService.GetRecentThreats().ToList();
            foreach (var threat in threats)
            {
                QuarantinedItems.Add(threat);
            }
        }
    }
}
