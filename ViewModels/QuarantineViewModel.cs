using CommunityToolkit.Mvvm.ComponentModel;
using RansomGuard.Models;
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

        public QuarantineViewModel()
        {
            _monitorService = new MockMonitorService();
            LoadData();
        }

        private void LoadData()
        {
            var threats = _monitorService.GetRecentThreats().ToList();
            foreach (var threat in threats)
            {
                QuarantinedItems.Add(threat);
            }
            TotalItems = QuarantinedItems.Count;
        }
    }
}
