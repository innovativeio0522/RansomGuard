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
        private const int MaxDashboardActivities = AppConstants.Limits.MaxActivityHistory / 10; // Top 10 for dashboard
        private const int MaxBufferSize = AppConstants.Limits.MaxBufferSize;
        private const int MaxActiveAlerts = AppConstants.Limits.MaxActiveAlerts;
        private int _baselineRiskScore = AppConstants.Limits.BaselineRiskScore;
        private const int MinutesThresholdForRecent = AppConstants.Limits.MinutesThresholdForRecent;
        
        private readonly ISystemMonitorService _monitorService;
        private DispatcherTimer? _telemetryTimer;
        private DispatcherTimer? _activityBufferTimer;
        private readonly ConcurrentQueue<FileActivity> _activityBuffer = new();
        private System.Collections.Specialized.NotifyCollectionChangedEventHandler? _alertsCollectionChangedHandler;
        private EventHandler? _telemetryTimerHandler;
        private EventHandler? _activityBufferTimerHandler;
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
        private ObservableCollection<LanPeer> _lanPeers = new();

        [ObservableProperty]
        private string _circuitBreakerStatus = "ARMED";

        [ObservableProperty]
        private string _activePeerCount = "0";

        [ObservableProperty]
        private string _behavioralStatus = "STABLE";

        [ObservableProperty]
        private string _protectionStatusText = "REAL-TIME ACTIVE";

        [ObservableProperty]
        private System.Windows.Media.Brush _protectionStatusColor;

        [ObservableProperty]
        private string _monitoringStatusText = "MONITORING ACTIVE";



        // Computed: ring stroke offset — circumference ~283, offset = 283*(1 - score/100)
        public double ThreatRingOffset => 283.0 * (1.0 - Math.Min(100, ThreatRiskScore) / 100.0);

        // Computed: "3 NEW" badge text bound to actual alert count
        public string NewAlertsText => ActiveAlerts.Count > 0 ? $"{ActiveAlerts.Count} NEW" : "CLEAR";



        public ObservableCollection<FileActivity> RecentActivities { get; } = new();
        public ObservableCollection<Threat> ActiveAlerts { get; } = new();

        public DashboardViewModel(ISystemMonitorService monitorService)
        {
            _monitorService = monitorService;
            
            // Initialize protection status color
            var secBrush = Application.Current?.Resources["SecondaryBrush"] as System.Windows.Media.Brush;
            ProtectionStatusColor = secBrush ?? System.Windows.Media.Brushes.Green;
            
            // Check initial connection status and update UI accordingly
            if (!_monitorService.IsConnected)
            {
                ProtectionStatusText = "PROTECTION DISABLED";
                var warnBrush = Application.Current?.Resources["WarningBrush"] as System.Windows.Media.Brush;
                ProtectionStatusColor = warnBrush ?? System.Windows.Media.Brushes.Orange;
                MonitoringStatusText = "SERVICE OFFLINE";
            }
            
            // Delay LoadData to ensure UI is fully initialized
            Application.Current?.Dispatcher.BeginInvoke(new Action(() => LoadData()), System.Windows.Threading.DispatcherPriority.Loaded);

            // Subscribe to live updates
            _monitorService.FileActivityDetected += OnFileActivityDetected;
            _monitorService.ThreatDetected += OnThreatDetected;
            _monitorService.ConnectionStatusChanged += OnConnectionStatusChanged;


            // Notify NewAlertsText when collection changes — store handler for later unsubscription
            _alertsCollectionChangedHandler = (s, e) => OnPropertyChanged(nameof(NewAlertsText));
            ActiveAlerts.CollectionChanged += _alertsCollectionChangedHandler;

            // Setup telemetry polling (every 2 seconds)
            _telemetryTimerHandler = (s, e) => UpdateTelemetry();
            _telemetryTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(AppConstants.Timers.DashboardTelemetryMs)
            };
            _telemetryTimer.Tick += _telemetryTimerHandler;
            _telemetryTimer.Start();

            // Setup activity buffer timer (every 500ms) for UI throttling
            _activityBufferTimerHandler = (s, e) => ProcessActivityBuffer();
            _activityBufferTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(AppConstants.Timers.ActivityBufferMs)
            };
            _activityBufferTimer.Tick += _activityBufferTimerHandler;
            _activityBufferTimer.Start();

            // Subscribe to configuration changes for instant UI refresh
            ConfigurationService.Instance.PathsChanged += OnPathsChanged;

            InitializeBaselineScore();

            _monitorService.LanPeerListUpdated += OnLanPeerListUpdated;
        }

        private void OnLanPeerListUpdated(LanPeerListUpdate update)
        {
            if (_disposed) return;
            Application.Current.Dispatcher.Invoke(() =>
            {
                LanPeers.Clear();
                foreach (var peer in update.Peers)
                {
                    LanPeers.Add(peer);
                }
                
                ActivePeerCount = update.Peers.Count.ToString();
                CircuitBreakerStatus = update.IsCircuitBroken ? "TRIPPED" : "ARMED";
                
                if (update.IsCircuitBroken && !IsPanicModeEngaged)
                {
                    IsPanicModeEngaged = true;
                    // Trigger some UI alert maybe?
                }
            });
        }

        private void OnConnectionStatusChanged(bool isConnected)
        {
            if (_disposed) return;
            Application.Current?.Dispatcher.Invoke(() =>
            {
                if (!isConnected)
                {
                    ProtectionStatusText = "PROTECTION DISABLED";
                    var warnBrush = Application.Current?.Resources["WarningBrush"] as System.Windows.Media.Brush;
                    ProtectionStatusColor = warnBrush ?? System.Windows.Media.Brushes.Orange;
                    MonitoringStatusText = "SERVICE OFFLINE";
                }
                else
                {
                    ProtectionStatusText = "REAL-TIME ACTIVE";
                    var secBrush = Application.Current?.Resources["SecondaryBrush"] as System.Windows.Media.Brush;
                    ProtectionStatusColor = secBrush ?? System.Windows.Media.Brushes.Green;
                    MonitoringStatusText = "MONITORING ACTIVE";
                }
            });
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
                ThreatsBlockedCount = threats.Count(t => 
                    t.ActionTaken == "Quarantined" || 
                    t.ActionTaken == "Quarantined (Auto)" || 
                    t.ActionTaken == "Terminated" || 
                    t.ActionTaken == "Mitigated" || 
                    t.ActionTaken == "Mitigated (Auto)");

                // 2. Reconcile ActiveAlerts Collection (deduplicated by path, prioritizing resolved status)
                var allRelevant = threats.Where(t => 
                    string.IsNullOrWhiteSpace(SearchQuery) || 
                    t.Path.Contains(SearchQuery, StringComparison.OrdinalIgnoreCase) ||
                    t.Name.Contains(SearchQuery, StringComparison.OrdinalIgnoreCase))
                    .GroupBy(t => t.Path.ToLowerInvariant())
                    .Select(g => g.OrderByDescending(t => 
                        t.ActionTaken == "Quarantined" || 
                        t.ActionTaken == "Quarantined (Auto)" || 
                        t.ActionTaken == "Mitigated" || 
                        t.ActionTaken == "Mitigated (Auto)" ? 2 : 
                        (t.ActionTaken == "Detected" || t.ActionTaken == "Active" ? 1 : 0))
                        .First())
                    .ToList();

                var freshActive = allRelevant.Where(t => 
                    t.ActionTaken == "Detected" || 
                    t.ActionTaken == "Awaiting Confirmation" || 
                    t.ActionTaken == "Active")
                    .ToList();

                // Marshal collection modifications to UI thread
                try
                {
                    Application.Current.Dispatcher.Invoke(() => {
                        // Remove resolved threats
                        var toRemove = ActiveAlerts.Where(a => !freshActive.Any(f => f.Path == a.Path)).ToList();
                        foreach (var r in toRemove) ActiveAlerts.Remove(r);

                        // Add new threats (respecting cap)
                        foreach (var f in freshActive)
                        {
                            if (!ActiveAlerts.Any(a => a.Path == f.Path))
                            {
                                if (ActiveAlerts.Count >= MaxActiveAlerts)
                                    ActiveAlerts.RemoveAt(ActiveAlerts.Count - 1);
                                ActiveAlerts.Insert(0, f);
                            }
                        }
                        
                        ActiveAlertsCount = ActiveAlerts.Count;
                    });
                }
                catch (Exception ex)
                {
                    FileLogger.LogError("ui_error.log", "[DashboardViewModel] Collection reconciliation error", ex);
                }

                var activities = _monitorService.GetRecentFileActivities()?.ToList() ?? new List<FileActivity>();
                RecentActivities.Clear();

                var consolidated = new List<FileActivity>();
                foreach (var activity in activities.OrderByDescending(a => a.Timestamp))
                {
                    if (activity == null) continue;

                    if (!string.IsNullOrWhiteSpace(SearchQuery) &&
                        !activity.FilePath.Contains(SearchQuery, StringComparison.OrdinalIgnoreCase) &&
                        !activity.ProcessName.Contains(SearchQuery, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    var existing = consolidated.FirstOrDefault(c =>
                        c.FilePath.Equals(activity.FilePath, StringComparison.OrdinalIgnoreCase) &&
                        Math.Abs((c.Timestamp - activity.Timestamp).TotalSeconds) < 2);

                    if (existing != null)
                    {
                        if (activity.Action == "CHANGED" && existing.Action == "CREATED")
                        {
                            int idx = consolidated.IndexOf(existing);
                            if (idx >= 0)
                            {
                                consolidated[idx] = activity;
                            }
                        }
                        continue;
                    }

                    consolidated.Add(activity);
                }

                foreach (var activity in consolidated.Take(MaxDashboardActivities))
                {
                    RecentActivities.Add(activity);
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

            var sortedBatch = batch.OrderByDescending(b => b.Timestamp).ToList();
            var uniqueNewItems = new List<FileActivity>();

            foreach (var item in sortedBatch)
            {
                // 1. Check against history (UI collection)
                var existingInHistory = RecentActivities.FirstOrDefault(r =>
                    r.Id == item.Id ||
                    (r.FilePath.Equals(item.FilePath, StringComparison.OrdinalIgnoreCase) &&
                     Math.Abs((r.Timestamp - item.Timestamp).TotalSeconds) < 2));

                if (existingInHistory != null)
                {
                    if (item.Action == "CHANGED" && existingInHistory.Action == "CREATED")
                    {
                        int index = RecentActivities.IndexOf(existingInHistory);
                        if (index >= 0)
                        {
                            RecentActivities[index] = item;
                        }
                    }
                    continue;
                }

                // 2. Check against what we've already accepted in this batch
                var existingInBatch = uniqueNewItems.FirstOrDefault(u =>
                    u.FilePath.Equals(item.FilePath, StringComparison.OrdinalIgnoreCase) &&
                    Math.Abs((u.Timestamp - item.Timestamp).TotalSeconds) < 2);

                if (existingInBatch != null)
                {
                    if (item.Action == "CHANGED" && existingInBatch.Action == "CREATED")
                    {
                        int batchIndex = uniqueNewItems.IndexOf(existingInBatch);
                        if (batchIndex >= 0)
                        {
                            uniqueNewItems[batchIndex] = item;
                        }
                    }
                    continue;
                }

                uniqueNewItems.Add(item);
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
            if (_disposed) return;

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
                    // Threat already in ActiveAlerts — check if it was just quarantined/ignored/mitigated
                    if (threat.ActionTaken == "Quarantined" || 
                        threat.ActionTaken == "Quarantined (Auto)" || 
                        threat.ActionTaken == "Ignored" || 
                        threat.ActionTaken == "Terminated" ||
                        threat.ActionTaken == "Mitigated" ||
                        threat.ActionTaken == "Mitigated (Auto)")
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
                    if (threat.ActionTaken == "Detected" || threat.ActionTaken == "Awaiting Confirmation" || threat.ActionTaken == "Active")
                    {
                        // Enforce cap — drop oldest if at limit
                        if (ActiveAlerts.Count >= MaxActiveAlerts)
                            ActiveAlerts.RemoveAt(ActiveAlerts.Count - 1);

                        ActiveAlerts.Insert(0, threat);
                        ActiveAlertsCount++;
                        UpdateRiskScore();

                        if (threat.Severity == ThreatSeverity.Critical)
                        {
                            // Global critical alerts are handled by MainViewModel
                        }
                    }
                    else if (threat.ActionTaken == "Quarantined" || 
                             threat.ActionTaken == "Quarantined (Auto)" || 
                             threat.ActionTaken == "Terminated" || 
                             threat.ActionTaken == "Mitigated" ||
                             threat.ActionTaken == "Mitigated (Auto)")
                    {
                        // Any handled threat counts towards mitigation
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
        private void ViewFileActivity() => NavigationRequested?.Invoke("FileActivity");

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
                if (_telemetryTimerHandler != null)
                {
                    _telemetryTimer.Tick -= _telemetryTimerHandler;
                    _telemetryTimerHandler = null;
                }
                _telemetryTimer = null;
            }
            
            // Stop and dispose activity buffer timer
            if (_activityBufferTimer != null)
            {
                _activityBufferTimer.Stop();
                if (_activityBufferTimerHandler != null)
                {
                    _activityBufferTimer.Tick -= _activityBufferTimerHandler;
                    _activityBufferTimerHandler = null;
                }
                _activityBufferTimer = null;
            }

            // Unsubscribe from collection changed
            if (_alertsCollectionChangedHandler != null)
            {
                ActiveAlerts.CollectionChanged -= _alertsCollectionChangedHandler;
                _alertsCollectionChangedHandler = null;
            }

            // Unsubscribe from events
            if (_monitorService != null)
            {
                _monitorService.FileActivityDetected -= OnFileActivityDetected;
                _monitorService.ThreatDetected -= OnThreatDetected;
                _monitorService.ConnectionStatusChanged -= OnConnectionStatusChanged;
                _monitorService.LanPeerListUpdated -= OnLanPeerListUpdated;
            }
            
            // Unsubscribe from configuration changes
            ConfigurationService.Instance.PathsChanged -= OnPathsChanged;
        }
        
        private void OnPathsChanged()
        {
            if (_disposed) return;
            Application.Current.Dispatcher.Invoke(() => {
                FilesMonitoredCount = _monitorService.GetMonitoredFilesCount().ToString("N0");
            });
        }
    }
}
