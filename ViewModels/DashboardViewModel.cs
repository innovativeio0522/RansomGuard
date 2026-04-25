using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RansomGuard.Core.Models;
using RansomGuard.Core.Interfaces;
using RansomGuard.Core.Helpers;
using RansomGuard.Core.Configuration;
using RansomGuard.Services;
using RansomGuard.Core.Services;
using RansomGuard.Core.IPC;
using System;
using System.IO;
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
        private const int MaxBufferSize = 1000; // Maximum buffer size to prevent unbounded growth
        private int _baselineRiskScore = 8;
        private const int MinutesThresholdForRecent = 60;
        
        private readonly ISystemMonitorService _monitorService;
        private DispatcherTimer? _telemetryTimer;
        private DispatcherTimer? _activityBufferTimer;
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
        private string _searchQuery = string.Empty;

        [ObservableProperty]
        private int _threatsBlockedCount = 0;

        [ObservableProperty]
        private int _activeAlertsCount = 0;

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
        private ObservableCollection<string> _activePaths = new();

        [ObservableProperty]
        private string _ramUsageText = "-- / -- GB";

        [ObservableProperty]
        private string _entropyScoreText = "2.4";



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



        // Computed: ring stroke offset — circumference ~283, offset = 283*(1 - score/100)
        public double ThreatRingOffset => 283.0 * (1.0 - Math.Min(100, ThreatRiskScore) / 100.0);

        // Computed: "3 NEW" badge text bound to actual alert count
        public string NewAlertsText => ActiveAlerts.Count > 0 ? $"{ActiveAlerts.Count} NEW" : "CLEAR";



        public ObservableCollection<FileActivity> RecentActivities { get; } = new();
        public ObservableCollection<Threat> ActiveAlerts { get; } = new();

        public DashboardViewModel(ISystemMonitorService monitorService)
        {
            _monitorService = monitorService;
            
            // Delay LoadData to ensure UI is fully initialized
            Application.Current.Dispatcher.BeginInvoke(new Action(() => LoadData()), System.Windows.Threading.DispatcherPriority.Loaded);

            // Subscribe to live updates
            _monitorService.FileActivityDetected += OnFileActivityDetected;
            _monitorService.ThreatDetected += OnThreatDetected;


            // Notify NewAlertsText when collection changes
            ActiveAlerts.CollectionChanged += (s, e) => OnPropertyChanged(nameof(NewAlertsText));

            // Setup telemetry polling (every 2 seconds)
            _telemetryTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(AppConstants.Timers.DashboardTelemetryMs)
            };
            _telemetryTimer.Tick += (s, e) => UpdateTelemetry();
            _telemetryTimer.Start();

            // Setup activity buffer timer (every 500ms) for UI throttling
            _activityBufferTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(AppConstants.Timers.ActivityBufferMs)
            };
            _activityBufferTimer.Tick += (s, e) => ProcessActivityBuffer();
            _activityBufferTimer.Start();

            // Subscribe to configuration changes for instant UI refresh
            ConfigurationService.Instance.PathsChanged += () => {
                Application.Current.Dispatcher.Invoke(() => {
                    FilesMonitoredCount = _monitorService.GetMonitoredFilesCount().ToString("N0");
                });
            };

            InitializeBaselineScore();
        }

        private void InitializeBaselineScore()
        {
            var config = ConfigurationService.Instance;
            var now = DateTime.Now;

            // If the score was never set or more than 1 hour has passed
            if (config.LastScoreUpdateTime == DateTime.MinValue || (now - config.LastScoreUpdateTime).TotalHours >= 1)
            {
                // Only update the "Base" score if there are no active threats (stable state)
                var threats = _monitorService.GetRecentThreats()?.ToList() ?? new List<Threat>();
                bool hasActiveThreats = threats.Any(t => t.ActionTaken == "Detected");

                if (!hasActiveThreats)
                {
                    _baselineRiskScore = new Random().Next(5, 13); // Randomly 5-12
                    config.BaseThreatScore = _baselineRiskScore;
                    config.LastScoreUpdateTime = now;
                    config.Save();
                }
                else
                {
                    _baselineRiskScore = config.BaseThreatScore;
                }
            }
            else
            {
                _baselineRiskScore = config.BaseThreatScore;
            }
        }

        partial void OnSearchQueryChanged(string value) => LoadData();

        private void LoadData()
        {
            try
            {
                FilesMonitoredCount = _monitorService.GetMonitoredFilesCount().ToString("N0");
                
                var threats = _monitorService.GetRecentThreats()?.ToList() ?? new List<Threat>();
                
                // Differentiate between "Blocked" (Remediated) and "Active" (Found but not yet handled)
                ThreatsBlockedCount = threats.Count(t => t.ActionTaken == "Quarantined" || t.ActionTaken == "Terminated");

                // 2. Reconcile ActiveAlerts Collection (avoiding duplicates and infinite growth)
                var freshActive = threats.Where(t => t.ActionTaken == "Detected" &&
                                                (string.IsNullOrWhiteSpace(SearchQuery) || 
                                                 t.Path.Contains(SearchQuery, StringComparison.OrdinalIgnoreCase) ||
                                                 t.Name.Contains(SearchQuery, StringComparison.OrdinalIgnoreCase)))
                                         .ToList();

                // Marshal collection modifications to UI thread
                Application.Current.Dispatcher.Invoke(() => {
                    // Remove resolved threats
                    var toRemove = ActiveAlerts.Where(a => !freshActive.Any(f => f.Path == a.Path)).ToList();
                    foreach (var r in toRemove) ActiveAlerts.Remove(r);

                    // Add new threats
                    foreach (var f in freshActive)
                    {
                        if (!ActiveAlerts.Any(a => a.Path == f.Path))
                        {
                            ActiveAlerts.Insert(0, f);
                        }
                    }
                    
                    ActiveAlertsCount = ActiveAlerts.Count;
                });

                var activities = _monitorService.GetRecentFileActivities()?.ToList() ?? new List<FileActivity>();
                RecentActivities.Clear();
                foreach (var activity in activities)
                {
                    if (activity != null)
                    {
                        if (string.IsNullOrWhiteSpace(SearchQuery) || 
                            activity.FilePath.Contains(SearchQuery, StringComparison.OrdinalIgnoreCase) ||
                            activity.ProcessName.Contains(SearchQuery, StringComparison.OrdinalIgnoreCase))
                        {
                            RecentActivities.Add(activity);
                        }
                    }
                }

                UpdateTelemetry();
            }
            catch (Exception ex)
            {
                FileLogger.LogError("ui_error.log", "[DashboardViewModel] LoadData error", ex);
                // Initialize with empty data on error
                FilesMonitoredCount = "0";
                ThreatsBlockedCount = 0;
            }
        }

        private void ProcessActivityBuffer()
        {
            if (_activityBuffer.IsEmpty) return;

            var batch = new List<FileActivity>();
            while (_activityBuffer.TryDequeue(out var activity))
            {
                batch.Add(activity);
            }

            var uniqueNewItems = new List<FileActivity>();
            foreach (var item in batch)
            {
                // Verify against what's already in the UI AND what we've already accepted from this batch
                bool isDuplicateInHistory = RecentActivities.Any(r => 
                    r.Id == item.Id || 
                    (r.FilePath == item.FilePath && Math.Abs((r.Timestamp - item.Timestamp).TotalSeconds) < 2));
                
                bool isDuplicateInBatch = uniqueNewItems.Any(u => 
                    u.FilePath == item.FilePath && Math.Abs((u.Timestamp - item.Timestamp).TotalSeconds) < 2);

                if (!isDuplicateInHistory && !isDuplicateInBatch)
                {
                    uniqueNewItems.Add(item);
                }
            }

            if (uniqueNewItems.Count == 0) return;

            foreach (var item in uniqueNewItems)
            {
                RecentActivities.Insert(0, item);
                if (RecentActivities.Count > MaxDashboardActivities)
                    RecentActivities.RemoveAt(RecentActivities.Count - 1);
            }
            
            FilesMonitoredCount = _monitorService.GetMonitoredFilesCount().ToString("N0");
        }

        private void UpdateTelemetry()
        {

            var telemetry = _monitorService.GetTelemetry();
            CpuUsagePercent = telemetry.CpuUsage;
            RamUsageBytes = telemetry.MemoryUsage;
            ActiveProcessesCount = telemetry.ProcessesCount;
            // Always reflect the config count (source of truth) — the service watcher count
            // can be lower if LocalSystem can't access certain drives (e.g. D:\).
            FilesMonitoredCount = ConfigurationService.Instance.MonitoredPaths.Count.ToString("N0");

            // Update active paths list from config (matches Settings page exactly)
            Application.Current.Dispatcher.Invoke(() => {
                var configPaths = ConfigurationService.Instance.MonitoredPaths;
                if (ActivePaths.Count != configPaths.Count ||
                    !configPaths.SequenceEqual(ActivePaths))
                {
                    ActivePaths.Clear();
                    foreach (var path in configPaths)
                        ActivePaths.Add(path);
                }
            });

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



        private void OnFileActivityDetected(FileActivity activity)
        {
            if (_disposed) return; // Check if disposed
            
            // Enforce buffer size limit to prevent unbounded growth
            if (_activityBuffer.Count >= MaxBufferSize)
            {
                // Drop oldest item
                _activityBuffer.TryDequeue(out _);
            }
            
            // Throttling: just enqueue and let the timer handle the UI update
            _activityBuffer.Enqueue(activity);
        }

        private void OnThreatDetected(Threat threat)
        {
            if (_disposed) return; // Check if disposed
            
            Application.Current.Dispatcher.Invoke(() =>
            {
                if (_disposed) return; // Check again in dispatcher
                var existing = ActiveAlerts.FirstOrDefault(a => string.Equals(a.Path, threat.Path, StringComparison.OrdinalIgnoreCase));

                if (existing != null)
                {
                    // Threat already in ActiveAlerts — check if it was just quarantined/ignored
                    if (threat.ActionTaken == "Quarantined" || threat.ActionTaken == "Ignored" || threat.ActionTaken == "Terminated")
                    {
                        ActiveAlerts.Remove(existing);
                        ActiveAlertsCount = Math.Max(0, ActiveAlertsCount - 1);
                        if (threat.ActionTaken != "Ignored") ThreatsBlockedCount++;
                        UpdateRiskScore();
                    }
                }
                else
                {
                    // New threat — add it if still active
                    if (threat.ActionTaken == "Detected")
                    {
                        ActiveAlerts.Insert(0, threat);
                        ActiveAlertsCount++;
                        UpdateRiskScore();

                        if (threat.Severity == ThreatSeverity.Critical)
                        {
                            var alert = new Views.ShieldUpAlert();
                            alert.Show();
                        }
                    }
                    else if (threat.ActionTaken == "Quarantined" || threat.ActionTaken == "Terminated")
                    {
                        ThreatsBlockedCount++;
                    }
                }
            });
        }

        private void UpdateRiskScore()
        {
            // Graduated: baseline 5–15 (randomised at startup), each alert adds ~10, capped at 95
            // Prevent integer overflow by clamping alert count to max 9 (9 * 10 = 90, + baseline max 15 = 105, capped at 95)
            int alertContribution = Math.Min(ActiveAlerts.Count, 9) * 10;
            ThreatRiskScore = Math.Min(95, _baselineRiskScore + alertContribution);
        }

        [RelayCommand]
        private void ViewAllLogs() => NavigationRequested?.Invoke("ThreatAlerts");

        [RelayCommand]
        private void IgnoreAlert(Threat threat)
        {
            if (threat == null) return;
            
            // Note: Ignore is a UI-level action - threat remains in database as "Active"
            // but is removed from the active alerts display
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
                
                // Manually remediated: Move count from Active -> Blocked
                if (ActiveAlerts.Contains(threat))
                {
                    ActiveAlertsCount = Math.Max(0, ActiveAlertsCount - 1);
                    ThreatsBlockedCount++;
                }
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

            // Stop and dispose telemetry timer
            if (_telemetryTimer != null)
            {
                _telemetryTimer.Stop();
                _telemetryTimer.Tick -= (s, e) => UpdateTelemetry();
                _telemetryTimer = null;
            }
            
            // Stop and dispose activity buffer timer
            if (_activityBufferTimer != null)
            {
                _activityBufferTimer.Stop();
                _activityBufferTimer.Tick -= (s, e) => ProcessActivityBuffer();
                _activityBufferTimer = null;
            }

            // Unsubscribe from events
            if (_monitorService != null)
            {
                _monitorService.FileActivityDetected -= OnFileActivityDetected;
                _monitorService.ThreatDetected -= OnThreatDetected;

            }
        }
    }
}
