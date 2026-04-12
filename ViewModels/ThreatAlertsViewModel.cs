using CommunityToolkit.Mvvm.ComponentModel;
using RansomGuard.Models;
using RansomGuard.Services;
using System.Collections.ObjectModel;

namespace RansomGuard.ViewModels
{
    public partial class ThreatAlertsViewModel : ViewModelBase
    {
        private readonly ISystemMonitorService _monitorService;

        [ObservableProperty]
        private int _highThreatsCount;

        [ObservableProperty]
        private int _mediumThreatsCount;

        [ObservableProperty]
        private int _lowThreatsCount;

        public ObservableCollection<Threat> Threats { get; } = new();

        public ThreatAlertsViewModel()
        {
            _monitorService = new MockMonitorService();
            LoadThreats();
        }

        private void LoadThreats()
        {
            var threats = _monitorService.GetRecentThreats().ToList();
            HighThreatsCount = threats.Count(t => t.Severity == ThreatSeverity.High || t.Severity == ThreatSeverity.Critical);
            MediumThreatsCount = threats.Count(t => t.Severity == ThreatSeverity.Medium);
            LowThreatsCount = threats.Count(t => t.Severity == ThreatSeverity.Low);

            foreach (var threat in threats)
            {
                Threats.Add(threat);
            }
        }
    }
}
