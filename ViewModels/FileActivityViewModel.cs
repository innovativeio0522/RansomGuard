using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RansomGuard.Core.Models;
using RansomGuard.Core.Interfaces;
using RansomGuard.Core.Services;
using RansomGuard.Services;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Threading;

namespace RansomGuard.ViewModels
{
    public partial class FileActivityViewModel : ViewModelBase, IDisposable
    {
        private const int MaxRecentActivities = 150;
        
        [ObservableProperty]
        private int _monitoredPathsCount;

        private readonly ISystemMonitorService _monitorService;
        private readonly DispatcherTimer _bufferTimer;
        private readonly ConcurrentQueue<FileActivity> _activityBuffer = new();
        private bool _disposed;

        public ObservableCollection<FileActivity> RecentActivities { get; } = new();
        private List<FileActivity> _allRecentActivities = new();

        [ObservableProperty] private string _searchQuery = string.Empty;
        [ObservableProperty] private string _eventsPerMin = "0";
        [ObservableProperty] private bool _isPaused;
        [ObservableProperty] private bool _isRealTimeProtectionEnabled = true;
        [ObservableProperty] private string _filterAction = "ALL";
        [ObservableProperty] private string _selectedPath = "ALL VOLUMES";
        [ObservableProperty] private string _riskFilter = "ALL";
        [ObservableProperty] private bool _hasRecentActivity = true;

        public ObservableCollection<string> MonitoredPaths { get; } = new() { "ALL VOLUMES" };

        // Histogram data for entropy distribution (7 buckets)
        public ObservableCollection<double> EntropyDistribution { get; } = new() { 0, 0, 0, 0, 0, 0, 0 }; 

        // Top I/O nodes
        public ObservableCollection<IoProcessNode> IoIntensiveNodes { get; } = new();

        private DateTime _lastMetricUpdate = DateTime.MinValue;

        public FileActivityViewModel(ISystemMonitorService monitorService)
        {
            _monitorService = monitorService;
            
            // Initial load of monitored paths from local config if possible
            foreach (var path in ConfigurationService.Instance.MonitoredPaths)
            {
                if (!MonitoredPaths.Contains(path))
                    MonitoredPaths.Add(path);
            }

            // Initial load - try fetching data immediately
            Refresh();

            // Subscribe to live updates
            _monitorService.FileActivityDetected += OnFileActivityDetected;

            _bufferTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
            _bufferTimer.Tick += (s, e) => ProcessBuffer();
            _bufferTimer.Start();

            // Handle connection changes to reload data when service becomes available
            _monitorService.ConnectionStatusChanged += OnConnectionStatusChanged;
            _monitorService.TelemetryUpdated += (tele) => {
                App.Current.Dispatcher.Invoke(() => {
                    IsRealTimeProtectionEnabled = tele.IsRealTimeProtectionEnabled;
                    MonitoredPathsCount = tele.MonitoredFilesCount;

                    // Update monitored paths if changed (always keep ALL VOLUMES at top)
                    if (tele.MonitoredPaths != null &&
                        (MonitoredPaths.Count != tele.MonitoredPaths.Length + 1 ||
                         !MonitoredPaths.Skip(1).SequenceEqual(tele.MonitoredPaths)))
                    {
                        var current = SelectedPath; // Preserve selection before clearing
                        MonitoredPaths.Clear();
                        MonitoredPaths.Add("ALL VOLUMES");
                        foreach (var path in tele.MonitoredPaths) MonitoredPaths.Add(path);
                        // Restore selection — fallback to ALL VOLUMES if it no longer exists
                        SelectedPath = MonitoredPaths.Contains(current) ? current : "ALL VOLUMES";
                    }
                });
            };
        }


        private void OnConnectionStatusChanged(bool isConnected)
        {
            if (isConnected) 
            {
                // Must marshal to UI thread to touch ObservableCollections
                System.Windows.Application.Current.Dispatcher.Invoke(() => Refresh());
            }
        }

        [RelayCommand]
        public void Refresh()
        {
            var activities = _monitorService.GetRecentFileActivities().ToList();
            System.Diagnostics.Debug.WriteLine($"[FileActivityVM] Refreshing. Found {activities.Count} historical activities.");
            _allRecentActivities.Clear();
            foreach (var a in activities) _allRecentActivities.Add(a);
            
            _activityBuffer.Clear(); // Clear buffer to avoid duplicates during reload
            
            // Update diagnostic info
            MonitoredPathsCount = _monitorService.GetMonitoredFilesCount();

            ApplyFilter();
            UpdateMetrics();
        }

        partial void OnSearchQueryChanged(string value) => ApplyFilter();
        partial void OnSelectedPathChanged(string value) => ApplyFilter();
        partial void OnRiskFilterChanged(string value) => ApplyFilter();

        private void OnFileActivityDetected(FileActivity activity)
        {
            if (IsPaused) return;
            _activityBuffer.Enqueue(activity);
        }

        private void ProcessBuffer()
        {
            if (_activityBuffer.IsEmpty && (DateTime.Now - _lastMetricUpdate).TotalSeconds < 2) return;

            var batch = new List<FileActivity>();
            while (_activityBuffer.TryDequeue(out var item))
                batch.Add(item);

            if (batch.Count > 0)
            {
                foreach (var item in batch)
                {
                    _allRecentActivities.Insert(0, item);
                    if (_allRecentActivities.Count > MaxRecentActivities)
                        _allRecentActivities.RemoveAt(_allRecentActivities.Count - 1);
                }
                ApplyFilter();
            }

            UpdateMetrics();
            _lastMetricUpdate = DateTime.Now;
        }

        [RelayCommand]
        public void SetFilterAction(string action)
        {
            FilterAction = action;
            ApplyFilter();
        }

        [RelayCommand]
        public void SetRiskFilter(string filter)
        {
            RiskFilter = filter;
            ApplyFilter();
        }

        private void ApplyFilter()
        {
            var filtered = _allRecentActivities.Where(a => 
            {
                // 1. Search Query Filter
                bool matchSearch = string.IsNullOrWhiteSpace(SearchQuery) || 
                                   a.FilePath.Contains(SearchQuery, StringComparison.OrdinalIgnoreCase) || 
                                   a.ProcessName.Contains(SearchQuery, StringComparison.OrdinalIgnoreCase);

                // 2. Action Filter
                bool matchAction = true;
                if (FilterAction != "ALL")
                {
                    if (FilterAction == "CREATED")
                    {
                        matchAction = a.Action == "CREATED";
                    }
                    else if (FilterAction == "MODIFIED")
                    {
                        matchAction = a.Action == "CHANGED";
                    }
                    else if (FilterAction == "DELETED")
                    {
                        matchAction = a.Action == "DELETED";
                    }
                    else if (FilterAction == "RENAMED")
                    {
                        matchAction = a.Action.StartsWith("RENAMED");
                    }

                }

                // 3. Path Filter
                bool matchPath = string.IsNullOrEmpty(SelectedPath) || SelectedPath == "ALL VOLUMES" || a.FilePath.StartsWith(SelectedPath, StringComparison.OrdinalIgnoreCase);

                // 4. Risk Filter
                bool matchRisk = true;
                if (RiskFilter == "LOW") matchRisk = !a.IsSuspicious && a.Entropy < 4.0;
                else if (RiskFilter == "MEDIUM") matchRisk = !a.IsSuspicious && a.Entropy >= 4.0;
                else if (RiskFilter == "HIGH") matchRisk = a.IsSuspicious;

                return matchSearch && matchAction && matchPath && matchRisk;
            }).ToList();

            RecentActivities.Clear();
            foreach (var a in filtered) RecentActivities.Add(a);
            
            // Update whether we have recent activity to show
            HasRecentActivity = RecentActivities.Count > 0;
        }


        private void UpdateMetrics()
        {
            if (_allRecentActivities.Count == 0) return;

            // 1. Calculate Events per Min
            var windowStart = DateTime.Now.AddMinutes(-1);
            int recentCount = _allRecentActivities.Count(a => a.Timestamp > windowStart);
            EventsPerMin = $"{recentCount * 1.5:F1}k"; // Simulated scale factor for "scanned / min" vs "logged"

            // 2. Entropy Distribution (0.0 to 8.0)
            var buckets = new double[7];
            foreach (var a in _allRecentActivities)
            {
                int idx = Math.Min((int)(a.Entropy), 6);
                buckets[idx]++;
            }
            
            double max = buckets.Max();
            if (max > 0)
            {
                for (int i = 0; i < 7; i++)
                {
                    // Scale to 20 to 100 height for UI
                    EntropyDistribution[i] = 20 + (buckets[i] / max) * 75;
                }
            }

            // 3. I/O Intensive Nodes
            var topNodes = _allRecentActivities
                .GroupBy(a => a.ProcessName)
                .Select(g => new IoProcessNode 
                { 
                    Name = g.Key, 
                    ActivityCount = g.Count(), 
                    VolumeMb = g.Count() * 0.42 // Estimation
                })
                .OrderByDescending(n => n.ActivityCount)
                .Take(2)
                .ToList();

            IoIntensiveNodes.Clear();
            foreach (var node in topNodes) IoIntensiveNodes.Add(node);
        }

        [RelayCommand]
        private void TogglePause() => IsPaused = !IsPaused;

        [RelayCommand]
        private void ExportCsv()
        {
            // Implementation...
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            _bufferTimer?.Stop();

            if (_monitorService != null)
            {
                _monitorService.FileActivityDetected -= OnFileActivityDetected;
                _monitorService.ConnectionStatusChanged -= OnConnectionStatusChanged;
            }
        }
    }

    public class IoProcessNode
    {
        public string Name { get; set; } = string.Empty;
        public int ActivityCount { get; set; }
        public double VolumeMb { get; set; }
    }
}
