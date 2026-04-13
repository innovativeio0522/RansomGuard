using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RansomGuard.Core.Models;
using RansomGuard.Core.Interfaces;
using RansomGuard.Services;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;

namespace RansomGuard.ViewModels
{
    public partial class DashboardViewModel : ViewModelBase
    {
        private readonly ISystemMonitorService _monitorService;
        private readonly DispatcherTimer _telemetryTimer;

        [ObservableProperty]
        private string _filesMonitoredCount = "0";

        [ObservableProperty]
        private bool _isHoneyPotActive = true;

        [ObservableProperty]
        private bool _isVssShieldActive = true;

        [ObservableProperty]
        private bool _isPanicModeEngaged = false;

        [ObservableProperty]
        private int _threatsBlockedCount = 0;

        [ObservableProperty]
        private int _activeProcessesCount = 0;

        [ObservableProperty]
        private double _threatRiskScore = 0;

        [ObservableProperty]
        private double _cpuUsagePercent = 0;

        [ObservableProperty]
        private double _ramUsageBytes = 0;

        [ObservableProperty]
        private string _lastScanText = "Never";

        [ObservableProperty]
        private string _networkLatency = "0.04ms";

        [ObservableProperty]
        private string _activeEndpoints = "1";

        [ObservableProperty]
        private string _encryptionLevel = "AES-256";

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(RescanButtonText))]
        private bool _isScanning = false;

        public string RescanButtonText => IsScanning ? "SCANNING..." : "RESCAN";

        public ObservableCollection<FileActivity> RecentActivities { get; } = new();
        public ObservableCollection<Threat> ActiveAlerts { get; } = new();

        public DashboardViewModel(ISystemMonitorService monitorService)
        {
            _monitorService = monitorService;
            LoadData();

            // Subscribe to live updates
            _monitorService.FileActivityDetected += OnFileActivityDetected;
            _monitorService.ThreatDetected += OnThreatDetected;

            // Setup telemetry polling (every 2 seconds)
            _telemetryTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(2)
            };
            _telemetryTimer.Tick += (s, e) => UpdateTelemetry();
            _telemetryTimer.Start();
        }

        private void LoadData()
        {
            FilesMonitoredCount = _monitorService.GetMonitoredFilesCount().ToString("N0");
            
            var threats = _monitorService.GetRecentThreats().ToList();
            ThreatsBlockedCount = threats.Count;
            ActiveAlerts.Clear();
            foreach (var threat in threats) ActiveAlerts.Add(threat);

            var activities = _monitorService.GetRecentFileActivities();
            RecentActivities.Clear();
            foreach (var activity in activities) RecentActivities.Add(activity);

            UpdateTelemetry();
        }

        private void UpdateTelemetry()
        {
            if (IsScanning) return; // Don't block during scan

            var telemetry = _monitorService.GetTelemetry();
            CpuUsagePercent = telemetry.CpuUsage;
            RamUsageBytes = telemetry.MemoryUsage;
            ActiveProcessesCount = telemetry.ProcessesCount;
            FilesMonitoredCount = telemetry.MonitoredFilesCount.ToString("N0");
            
            IsHoneyPotActive = telemetry.IsHoneyPotActive;
            IsVssShieldActive = telemetry.IsVssShieldActive;
            IsPanicModeEngaged = telemetry.IsPanicModeActive;
            
            var lastScan = _monitorService.GetLastScanTime();
            var diff = DateTime.Now - lastScan;
            
            if (diff.TotalMinutes < 1) LastScanText = "Just now";
            else if (diff.TotalMinutes < 60) LastScanText = $"{(int)diff.TotalMinutes} mins ago";
            else LastScanText = $"{(int)diff.TotalHours} hours ago";

            // Decorative footer telemetry
            var rand = new Random();
            NetworkLatency = $"{(rand.NextDouble() * 0.05 + 0.02):F2}ms";
            ActiveEndpoints = "1"; // Local machine
            EncryptionLevel = "AES-256";

            UpdateRiskScore();
        }

        [RelayCommand]
        private async Task Rescan()
        {
            if (IsScanning) return;

            IsScanning = true;
            try
            {
                await _monitorService.PerformQuickScan();
            }
            finally
            {
                IsScanning = false;
                UpdateTelemetry();
            }
        }

        private void OnFileActivityDetected(FileActivity activity)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                RecentActivities.Insert(0, activity);
                if (RecentActivities.Count > 10) RecentActivities.RemoveAt(10);
                
                FilesMonitoredCount = _monitorService.GetMonitoredFilesCount().ToString("N0");
            });
        }

        private void OnThreatDetected(Threat threat)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                // Avoid duplicates in active alerts during scan
                if (!ActiveAlerts.Any(a => a.Path == threat.Path))
                {
                    ActiveAlerts.Insert(0, threat);
                    ThreatsBlockedCount++;
                    UpdateRiskScore();

                    // TRAP: Trigger Full-Screen Alert for Critical Threats
                    if (threat.Severity == ThreatSeverity.Critical)
                    {
                        var alert = new Views.ShieldUpAlert();
                        alert.Show();
                    }
                }
            });
        }

        private void UpdateRiskScore()
        {
            ThreatRiskScore = ActiveAlerts.Count > 0 ? 65 : 12;
        }
    }
}
