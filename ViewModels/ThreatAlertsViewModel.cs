using CommunityToolkit.Mvvm.ComponentModel;
using RansomGuard.Core.Models;
using RansomGuard.Core.Interfaces;
using RansomGuard.Services;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;

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

        public ThreatAlertsViewModel(ISystemMonitorService monitorService)
        {
            _monitorService = monitorService;
            LoadThreats();

            _monitorService.ThreatDetected += OnThreatDetected;
        }

        private void LoadThreats()
        {
            var threats = _monitorService.GetRecentThreats().ToList();
            Threats.Clear();
            HighThreatsCount = threats.Count(t => t.Severity == ThreatSeverity.High || t.Severity == ThreatSeverity.Critical);
            MediumThreatsCount = threats.Count(t => t.Severity == ThreatSeverity.Medium);
            LowThreatsCount = threats.Count(t => t.Severity == ThreatSeverity.Low);

            foreach (var threat in threats)
            {
                Threats.Add(threat);
            }
        }

        private void OnThreatDetected(Threat threat)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                Threats.Insert(0, threat);
                
                if (threat.Severity == ThreatSeverity.High || threat.Severity == ThreatSeverity.Critical) HighThreatsCount++;
                else if (threat.Severity == ThreatSeverity.Medium) MediumThreatsCount++;
                else LowThreatsCount++;
            });
        }
    }
}
