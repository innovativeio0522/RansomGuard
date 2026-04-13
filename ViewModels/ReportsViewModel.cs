using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.Linq;
using RansomGuard.Core.Services;
using RansomGuard.Core.Interfaces;

namespace RansomGuard.ViewModels
{
    public partial class ReportsViewModel : ViewModelBase
    {
        private readonly ISystemMonitorService? _monitorService;

        [ObservableProperty]
        private string _lastScanDate = "Never";

        [ObservableProperty]
        private int _totalScans = 0;

        [ObservableProperty]
        private int _securityScore = 0;

        public ReportsViewModel(ISystemMonitorService? monitorService = null)
        {
            _monitorService = monitorService;
            LoadData();
        }

        private void LoadData()
        {
            // Load last scan time from configuration
            var lastScan = ConfigurationService.Instance.LastScanTime;
            if (lastScan != DateTime.MinValue)
            {
                LastScanDate = lastScan.ToString("MMM dd, yyyy HH:mm");
                
                // Estimate total scans based on how long ago first scan was
                // Assume average of 1 scan per day
                var daysSinceFirstScan = (DateTime.Now - lastScan).TotalDays;
                TotalScans = Math.Max(1, (int)daysSinceFirstScan);
            }
            else
            {
                LastScanDate = "Never";
                TotalScans = 0;
            }

            // Calculate security score based on threat data and entropy
            CalculateSecurityScore();
        }

        private void CalculateSecurityScore()
        {
            if (_monitorService == null)
            {
                SecurityScore = 85; // Default score when no monitor service
                return;
            }

            try
            {
                var telemetry = _monitorService.GetTelemetry();
                var threats = _monitorService.GetRecentThreats().ToList();

                // Start with perfect score
                int score = 100;

                // Deduct points for threats
                int criticalThreats = threats.Count(t => t.Severity == Core.Models.ThreatSeverity.Critical);
                int highThreats = threats.Count(t => t.Severity == Core.Models.ThreatSeverity.High);
                int mediumThreats = threats.Count(t => t.Severity == Core.Models.ThreatSeverity.Medium);

                score -= criticalThreats * 15; // -15 per critical threat
                score -= highThreats * 8;      // -8 per high threat
                score -= mediumThreats * 3;    // -3 per medium threat

                // Deduct points for high entropy (potential encryption activity)
                if (telemetry.EntropyScore > 5.5)
                {
                    score -= 10;
                }
                else if (telemetry.EntropyScore > 4.5)
                {
                    score -= 5;
                }

                // Bonus points for active shields
                if (telemetry.IsHoneyPotActive) score += 2;
                if (telemetry.IsVssShieldActive) score += 2;

                // Clamp score between 0 and 100
                SecurityScore = Math.Clamp(score, 0, 100);
            }
            catch
            {
                SecurityScore = 85; // Fallback score on error
            }
        }
    }
}
