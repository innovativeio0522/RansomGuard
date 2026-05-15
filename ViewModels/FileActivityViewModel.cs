using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RansomGuard.Core.Models;
using RansomGuard.Core.Interfaces;
using RansomGuard.Core.Services;
using RansomGuard.Core.Configuration;
using RansomGuard.Services;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Threading;

namespace RansomGuard.ViewModels
{
    public partial class FileActivityViewModel : ViewModelBase, IDisposable
    {
        private const string AllVolumesLabel = "ALL VOLUMES";
        private const int MaxRecentActivities = 150;
        private const int MaxBufferSize = 1000; // Maximum buffer size to prevent unbounded growth
        private static readonly IReadOnlyDictionary<string, string> KnownFolderNames = CreateKnownFolderNames();
        
        [ObservableProperty]
        private int _monitoredPathsCount;

        private readonly ISystemMonitorService _monitorService;
        private DispatcherTimer? _bufferTimer;
        private readonly ConcurrentQueue<FileActivity> _activityBuffer = new();
        private bool _disposed;

        public ObservableCollection<FileActivity> RecentActivities { get; } = new();
        private List<FileActivity> _allRecentActivities = new();

        [ObservableProperty] private string _searchQuery = string.Empty;
        [ObservableProperty] private string _eventsPerMin = "0";
        [ObservableProperty] private bool _isPaused;
        [ObservableProperty] private bool _isRealTimeProtectionEnabled = true;
        [ObservableProperty] private string _filterAction = "ALL";
        [ObservableProperty] private PathFilterOption? _selectedPath = PathFilterOption.AllVolumes;
        [ObservableProperty] private string _riskFilter = "ALL";
        [ObservableProperty] private bool _hasRecentActivity = true;

        public ObservableCollection<PathFilterOption> MonitoredPaths { get; } = new() { PathFilterOption.AllVolumes };

        // Histogram data for entropy distribution (7 buckets)
        public ObservableCollection<double> EntropyDistribution { get; } = new() { 0, 0, 0, 0, 0, 0, 0 }; 

        // Top I/O nodes
        public ObservableCollection<IoProcessNode> IoIntensiveNodes { get; } = new();

        private DateTime _lastMetricUpdate = DateTime.MinValue;

        public FileActivityViewModel(ISystemMonitorService monitorService)
        {
            _monitorService = monitorService;
            
            // Initial load of monitored paths from local config if possible
            UpdateMonitoredPaths(ConfigurationService.Instance.MonitoredPaths);

            // Initial load - try fetching data immediately
            if (_monitorService.IsConnected)
            {
                Refresh();
            }
            else
            {
                // If not connected yet, the OnConnectionStatusChanged handler will trigger it
                Refresh(); 
            }

            // Subscribe to live updates
            _monitorService.FileActivityDetected += OnFileActivityDetected;

            _bufferTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(AppConstants.Timers.ActivityBufferMs) };
            _bufferTimer.Tick += (s, e) => ProcessBuffer();
            _bufferTimer.Start();

            // Handle connection changes to reload data when service becomes available
            _monitorService.ConnectionStatusChanged += OnConnectionStatusChanged;
            _monitorService.TelemetryUpdated += (tele) => {
                App.Current.Dispatcher.Invoke(() => {
                    IsRealTimeProtectionEnabled = tele.IsRealTimeProtectionEnabled;
                    MonitoredPathsCount = tele.MonitoredFilesCount;

                    // Update monitored paths if changed (always keep ALL VOLUMES at top)
                    if (tele.MonitoredPaths != null)
                    {
                        UpdateMonitoredPaths(tele.MonitoredPaths);
                    }
                });
            };
        }


        private async void OnConnectionStatusChanged(bool isConnected)
        {
            if (_disposed) return; // Check if disposed
            
            if (isConnected)
            {
                // Snapshots (FileActivitySnapshot, ThreatDetectedSnapshot) are sent by the
                // server immediately after the handshake and arrive as individual IPC packets.
                // We must wait for them to be processed before calling Refresh(), otherwise
                // GetRecentFileActivities() returns an empty list.
                await Task.Delay(2000).ConfigureAwait(false);
                
                if (_disposed) return; // Check again after delay
                
                System.Windows.Application.Current.Dispatcher.Invoke(() => 
                {
                    if (!_disposed) // Check in dispatcher
                    {
                        Refresh();
                    }
                });
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
        partial void OnSelectedPathChanged(PathFilterOption? value) => ApplyFilter();
        partial void OnRiskFilterChanged(string value) => ApplyFilter();

        private void OnFileActivityDetected(FileActivity activity)
        {
            if (IsPaused || _disposed) return; // Check if disposed
            
            // Enforce buffer size limit to prevent unbounded growth
            if (_activityBuffer.Count >= MaxBufferSize)
            {
                // Drop oldest item
                _activityBuffer.TryDequeue(out _);
            }
            
            _activityBuffer.Enqueue(activity);
        }

        private void ProcessBuffer()
        {
            if (_activityBuffer.IsEmpty && (DateTime.Now - _lastMetricUpdate).TotalSeconds < 2) return;

            var batch = new List<FileActivity>();
            while (_activityBuffer.TryDequeue(out var item))
                batch.Add(item);

            var uniqueNewItems = new List<FileActivity>();
            foreach (var item in batch)
            {
                // Verify against the total history AND what we've already picked in this batch
                bool isDuplicateInHistory = _allRecentActivities.Any(r => 
                    r.Id == item.Id || 
                    (r.FilePath == item.FilePath && r.Action == item.Action && Math.Abs((r.Timestamp - item.Timestamp).TotalSeconds) < 2));
                
                bool isDuplicateInBatch = uniqueNewItems.Any(u => 
                    u.FilePath == item.FilePath && u.Action == item.Action && Math.Abs((u.Timestamp - item.Timestamp).TotalSeconds) < 2);

                if (!isDuplicateInHistory && !isDuplicateInBatch)
                {
                    uniqueNewItems.Add(item);
                }
            }

            if (uniqueNewItems.Count > 0)
            {
                foreach (var item in uniqueNewItems)
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
                bool matchPath = SelectedPath == null ||
                                 SelectedPath.IsAllVolumes ||
                                 string.IsNullOrWhiteSpace(SelectedPath.Path) ||
                                 a.FilePath.StartsWith(SelectedPath.Path, StringComparison.OrdinalIgnoreCase);

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

        private void UpdateMonitoredPaths(IEnumerable<string>? paths)
        {
            var currentPath = SelectedPath?.Path;
            var options = BuildPathFilterOptions(paths);

            MonitoredPaths.Clear();
            foreach (var option in options)
            {
                MonitoredPaths.Add(option);
            }

            SelectedPath = MonitoredPaths.FirstOrDefault(option => PathsEqual(option.Path, currentPath))
                ?? MonitoredPaths.FirstOrDefault()
                ?? PathFilterOption.AllVolumes;
        }

        private static List<PathFilterOption> BuildPathFilterOptions(IEnumerable<string>? paths)
        {
            var options = new List<PathFilterOption> { PathFilterOption.AllVolumes };
            var seenPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            if (paths == null)
            {
                return options;
            }

            foreach (var rawPath in paths)
            {
                var normalizedPath = NormalizePath(rawPath);
                if (string.IsNullOrWhiteSpace(normalizedPath) || !seenPaths.Add(normalizedPath))
                {
                    continue;
                }

                options.Add(new PathFilterOption(CreateDisplayName(normalizedPath), normalizedPath));
            }

            return options;
        }

        private static string CreateDisplayName(string path)
        {
            if (KnownFolderNames.TryGetValue(path, out var knownFolderName))
            {
                return knownFolderName;
            }

            var trimmedPath = path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var folderName = Path.GetFileName(trimmedPath);
            if (!string.IsNullOrWhiteSpace(folderName))
            {
                return folderName;
            }

            return path;
        }

        private static string NormalizePath(string? path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return string.Empty;
            }

            var trimmed = path.Trim();

            // Keep drive roots such as C:\ intact while trimming trailing separators elsewhere.
            if (trimmed.Length <= 3 && trimmed.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal))
            {
                return trimmed;
            }

            return trimmed.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }

        private static bool PathsEqual(string? left, string? right)
        {
            return string.Equals(NormalizePath(left), NormalizePath(right), StringComparison.OrdinalIgnoreCase);
        }

        private static IReadOnlyDictionary<string, string> CreateKnownFolderNames()
        {
            var folders = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            AddKnownFolder(folders, Environment.SpecialFolder.DesktopDirectory, "Desktop");
            AddKnownFolder(folders, Environment.SpecialFolder.MyDocuments, "Documents");
            AddKnownFolder(folders, Environment.SpecialFolder.MyMusic, "Music");
            AddKnownFolder(folders, Environment.SpecialFolder.MyPictures, "Pictures");
            AddKnownFolder(folders, Environment.SpecialFolder.MyVideos, "Videos");
            AddKnownFolder(folders, Environment.SpecialFolder.UserProfile, "User Profile");

            var downloadsPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "Downloads");
            if (Directory.Exists(downloadsPath))
            {
                folders[NormalizePath(downloadsPath)] = "Downloads";
            }

            var oneDrivePath = Environment.GetEnvironmentVariable("OneDrive");
            if (!string.IsNullOrWhiteSpace(oneDrivePath) && Directory.Exists(oneDrivePath))
            {
                folders[NormalizePath(oneDrivePath)] = "OneDrive";
            }

            return folders;
        }

        private static void AddKnownFolder(
            IDictionary<string, string> folders,
            Environment.SpecialFolder specialFolder,
            string displayName)
        {
            var path = Environment.GetFolderPath(specialFolder);
            if (!string.IsNullOrWhiteSpace(path) && Directory.Exists(path))
            {
                folders[NormalizePath(path)] = displayName;
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            // Stop and dispose buffer timer
            if (_bufferTimer != null)
            {
                _bufferTimer.Stop();
                _bufferTimer.Tick -= (s, e) => ProcessBuffer();
                _bufferTimer = null;
            }

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

    public class PathFilterOption
    {
        public static PathFilterOption AllVolumes { get; } = new("ALL VOLUMES", string.Empty, "Show activity across all monitored drives and protected folders.");

        public PathFilterOption(string displayName, string path, string? toolTip = null)
        {
            DisplayName = displayName;
            Path = path;
            ToolTip = toolTip ?? (string.IsNullOrWhiteSpace(path) ? displayName : path);
        }

        public string DisplayName { get; }
        public string Path { get; }
        public string ToolTip { get; }
        public bool IsAllVolumes => string.IsNullOrWhiteSpace(Path);
    }
}
