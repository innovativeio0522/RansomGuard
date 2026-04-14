using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RansomGuard.Core.Models;
using RansomGuard.Core.Interfaces;
using RansomGuard.Core.Helpers;
using RansomGuard.Services;
using System;
using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;

namespace RansomGuard.ViewModels
{
    public partial class DashboardViewModel : ViewModelBase, IDisposable
    {
        private const int MaxDashboardActivities = 10;
        private const int MinutesThresholdForRecent = 60;
        
        private readonly ISystemMonitorService _monitorService;
        private readonly DispatcherTimer _telemetryTimer;
        private readonly DispatcherTimer _activityBufferTimer;
        private readonly ConcurrentQueue<FileActivity> _activityBuffer = new();
        private bool _disposed;

        // Set by MainViewModel so the dashboard can trigger navigation
        public Action<string>? NavigationRequested { get; set; }

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
        [NotifyPropertyChangedFor(nameof(ThreatRingOffset))]
        private double _threatRiskScore = 0;

        [ObservableProperty]
        private double _cpuUsagePercent = 0;

        [ObservableProperty]
        private double _ramUsageBytes = 0;

        [ObservableProperty]
        private double _ramUsagePercent = 0;

        [ObservableProperty]
        private string _ramUsageText = "-- / -- GB";

        [ObservableProperty]
        private string _entropyScoreText = "2.4";

        [ObservableProperty]
        private string _lastScanText = "Never";

        [ObservableProperty]
        private string _networkLatency = "0.04ms";

        [ObservableProperty]
        private string _activeEndpoints = "1";

        [ObservableProperty]
        private string _encryptionLevel = "AES-256";

        [ObservableProperty]
        private string _filesPerHourText = "+0 / HOUR";

        [ObservableProperty]
        private string _heuristicsStatus = "PASS";

        [ObservableProperty]
        private string _behavioralStatus = "STABLE";

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(RescanButtonText))]
        private bool _isScanning = false;

        // Computed: ring stroke offset — circumference ~283, offset = 283*(1 - score/100)
        public double ThreatRingOffset => 283.0 * (1.0 - Math.Min(100, ThreatRiskScore) / 100.0);

        // Computed: "3 NEW" badge text bound to actual alert count
        public string NewAlertsText => ActiveAlerts.Count > 0 ? $"{ActiveAlerts.Count} NEW" : "CLEAR";

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

            // Notify NewAlertsText when collection changes
            ActiveAlerts.CollectionChanged += (s, e) => OnPropertyChanged(nameof(NewAlertsText));

            // Setup telemetry polling (every 2 seconds)
            _telemetryTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(2)
            };
            _telemetryTimer.Tick += (s, e) => UpdateTelemetry();
            _telemetryTimer.Start();

            // Setup activity buffer timer (every 500ms) for UI throttling
            _activityBufferTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(500)
            };
            _activityBufferTimer.Tick += (s, e) => ProcessActivityBuffer();
            _activityBufferTimer.Start();
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

        private void ProcessActivityBuffer()
        {
            if (_activityBuffer.IsEmpty) return;

            var newItems = new List<FileActivity>();
            while (_activityBuffer.TryDequeue(out var activity))
            {
                newItems.Add(activity);
            }

            // Insert new items at the beginning, maintaining order
            foreach (var item in newItems)
            {
                RecentActivities.Insert(0, item);
                if (RecentActivities.Count > MaxDashboardActivities)
                    RecentActivities.RemoveAt(RecentActivities.Count - 1);
            }
            
            FilesMonitoredCount = _monitorService.GetMonitoredFilesCount().ToString("N0");
        }

        private void UpdateTelemetry()
        {
            if (IsScanning) return;

            var telemetry = _monitorService.GetTelemetry();
            CpuUsagePercent = telemetry.CpuUsage;
            RamUsageBytes = telemetry.MemoryUsage;
            ActiveProcessesCount = telemetry.ProcessesCount;
            FilesMonitoredCount = telemetry.MonitoredFilesCount.ToString("N0");

            IsHoneyPotActive = telemetry.IsHoneyPotActive;
            IsVssShieldActive = telemetry.IsVssShieldActive;
            IsPanicModeEngaged = telemetry.IsPanicModeActive;

            // Updated: rely on service-provided RAM stats to avoid UI thread blocking
            if (telemetry.SystemRamTotalMb > 0)
            {
                double usedGb = telemetry.SystemRamUsedMb / 1024.0;
                double totalGb = telemetry.SystemRamTotalMb / 1024.0;
                RamUsageText = $"{usedGb:F1} / {totalGb:F1} GB";
                RamUsagePercent = (telemetry.SystemRamUsedMb / telemetry.SystemRamTotalMb) * 100.0;
            }

            // Entropy
            if (telemetry.EntropyScore > 0)
                EntropyScoreText = $"{telemetry.EntropyScore:F1}";

            var lastScan = _monitorService.GetLastScanTime();
            if (lastScan == DateTime.MinValue)
            {
                LastScanText = "Never";
            }
            else
            {
                var diff = DateTime.Now - lastScan;
                if (diff.TotalMinutes < 1) LastScanText = "Just now";
                else if (diff.TotalMinutes < MinutesThresholdForRecent) LastScanText = $"{(int)diff.TotalMinutes} mins ago";
                else LastScanText = $"{(int)diff.TotalHours} hours ago";
            }

            // Dynamic telemetry (previously hardcoded)
            NetworkLatency = telemetry.NetworkLatencyMs > 0
                ? $"{telemetry.NetworkLatencyMs:F2}ms"
                : "<1ms";
            ActiveEndpoints = telemetry.ActiveEndpointsCount.ToString();
            EncryptionLevel = string.IsNullOrEmpty(telemetry.EncryptionLevel) ? "AES-256" : telemetry.EncryptionLevel;
            FilesPerHourText = telemetry.FilesPerHour >= 0
                ? $"+{telemetry.FilesPerHour:N0} / HOUR"
                : "0 / HOUR";

            // Heuristics: flag if entropy is abnormally high (>5.5 H/b suggests encryption activity)
            HeuristicsStatus = telemetry.EntropyScore > 5.5 ? "ALERT" : "PASS";

            // Behavioral: flag if there are active alerts
            BehavioralStatus = ActiveAlerts.Count > 0 ? "ALERT" : "STABLE";

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
            // Throttling: just enqueue and let the timer handle the UI update
            _activityBuffer.Enqueue(activity);
        }

        private void OnThreatDetected(Threat threat)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                if (!ActiveAlerts.Any(a => a.Path == threat.Path))
                {
                    ActiveAlerts.Insert(0, threat);
                    ThreatsBlockedCount++;
                    UpdateRiskScore();

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
            // Graduated: 0 alerts = 12, each alert adds ~10, capped at 95
            ThreatRiskScore = Math.Min(95, 12 + ActiveAlerts.Count * 10);
        }

        [RelayCommand]
        private void ViewAllLogs() => NavigationRequested?.Invoke("ThreatAlerts");

        [RelayCommand]
        private void IgnoreAlert(Threat threat)
        {
            if (threat == null) return;
            ActiveAlerts.Remove(threat);
            UpdateRiskScore();
        }

        [RelayCommand]
        private async Task QuarantineAlert(Threat threat)
        {
            if (threat == null) return;
            try
            {
                await _monitorService.QuarantineFile(threat.Path);
                threat.ActionTaken = "Quarantined";
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"QuarantineAlert error: {ex.Message}");
            }
            finally
            {
                ActiveAlerts.Remove(threat);
                UpdateRiskScore();
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            // Stop and dispose timer
            _telemetryTimer?.Stop();
            _activityBufferTimer?.Stop();

            // Unsubscribe from events
            if (_monitorService != null)
            {
                _monitorService.FileActivityDetected -= OnFileActivityDetected;
                _monitorService.ThreatDetected -= OnThreatDetected;
            }
        }
    }
}
