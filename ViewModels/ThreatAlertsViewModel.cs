using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RansomGuard.Core.Models;
using RansomGuard.Core.Interfaces;
using RansomGuard.Core.Configuration;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;

namespace RansomGuard.ViewModels
{
    public partial class ThreatAlertsViewModel : ViewModelBase, IDisposable
    {
        private readonly ISystemMonitorService _monitorService;
        private List<Threat> _allThreats = new();
        private readonly object _threatsLock = new();
        private const int MaxThreats = AppConstants.Limits.MaxThreats;
        private const string EmptyAlertsText = "No alerts";
        private bool _disposed;
        private System.Windows.Threading.DispatcherTimer _refreshTimer;

        [ObservableProperty]
        private int _criticalThreatsCount;

        [ObservableProperty]
        private int _highThreatsCount;

        [ObservableProperty]
        private int _mediumThreatsCount;

        [ObservableProperty]
        private int _lowThreatsCount;

        [ObservableProperty]
        private int _selectedSeverityIndex = 0;

        [ObservableProperty]
        private int _selectedDateRangeIndex = 0;

        [ObservableProperty]
        private string _paginationText = EmptyAlertsText;

        [ObservableProperty]
        private bool _hasPrevious = false;

        [ObservableProperty]
        private bool _hasNext = false;

        [ObservableProperty]
        private string _searchQuery = string.Empty;

        private int _currentPage = 0;
        private const int PageSize = AppConstants.Limits.PageSize;

        public ObservableCollection<Threat> Threats { get; } = new();

        private EventHandler? _refreshTimerHandler;

        public ThreatAlertsViewModel(ISystemMonitorService monitorService)
        {
            _monitorService = monitorService;
            
            // Subscribe to live events
            _monitorService.ThreatDetected += OnThreatDetected;

            LoadThreats();

            // Set up an auto-refresh timer — use named handler so it can be unsubscribed in Dispose
            _refreshTimerHandler = (s, e) => LoadThreats();
            _refreshTimer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(AppConstants.Timers.ThreatAlertsRefreshMs)
            };
            _refreshTimer.Tick += _refreshTimerHandler;
            _refreshTimer.Start();
        }

        private void LoadThreats()
        {
            if (_disposed) return;
            
            var fresh = _monitorService.GetRecentThreats()?.ToList() ?? new List<Threat>();

            lock (_threatsLock)
            {
                // Enforce cap — keep only the most recent MaxThreats entries
                _allThreats = fresh.Count > MaxThreats
                    ? fresh.Take(MaxThreats).ToList()
                    : fresh;
            }
            RefreshCounts();
            ApplyFilters();
        }

        private void RefreshCounts()
        {
            if (_disposed) return; // Check if disposed
            
            IEnumerable<Threat> activeThreats;
            lock (_threatsLock)
            {
                activeThreats = _allThreats.Where(t => 
                    t.ActionTaken != "Quarantined" && 
                    t.ActionTaken != "Quarantined (Auto)" && 
                    t.ActionTaken != "Ignored" && 
                    t.ActionTaken != "Mitigated" && 
                    t.ActionTaken != "Mitigated (Auto)").ToList();
            }
            
            CriticalThreatsCount = activeThreats.Count(t => t.Severity == ThreatSeverity.Critical);
            HighThreatsCount = activeThreats.Count(t => t.Severity == ThreatSeverity.High);
            MediumThreatsCount = activeThreats.Count(t => t.Severity == ThreatSeverity.Medium);
            LowThreatsCount = activeThreats.Count(t => t.Severity == ThreatSeverity.Low);
        }

        private void ApplyFilters()
        {
            if (_disposed) return; // Check if disposed
            
            IEnumerable<Threat> filtered;
            lock (_threatsLock)
            {
                filtered = _allThreats.AsEnumerable();
            }

            // Always exclude things that have already been handled
            filtered = filtered.Where(t => 
                t.ActionTaken != "Quarantined" && 
                t.ActionTaken != "Quarantined (Auto)" && 
                t.ActionTaken != "Ignored" && 
                t.ActionTaken != "Mitigated" && 
                t.ActionTaken != "Mitigated (Auto)");

            // Search query filter
            if (!string.IsNullOrWhiteSpace(SearchQuery))
            {
                filtered = filtered.Where(t => 
                    t.Path.Contains(SearchQuery, StringComparison.OrdinalIgnoreCase) || 
                    t.Name.Contains(SearchQuery, StringComparison.OrdinalIgnoreCase));
            }

            // Severity filter
            filtered = SelectedSeverityIndex switch
            {
                1 => filtered.Where(t => t.Severity == ThreatSeverity.Critical),
                2 => filtered.Where(t => t.Severity == ThreatSeverity.High),
                3 => filtered.Where(t => t.Severity == ThreatSeverity.Medium),
                4 => filtered.Where(t => t.Severity == ThreatSeverity.Low),
                _ => filtered
            };

            // Date range filter
            var cutoff = SelectedDateRangeIndex switch
            {
                1 => DateTime.Now.AddDays(-7),
                2 => DateTime.Now.AddDays(-30),
                _ => DateTime.Now.AddHours(-24)
            };
            filtered = filtered.Where(t => t.Timestamp >= cutoff);

            var list = filtered.OrderByDescending(t => t.Timestamp).ToList();
            int total = list.Count;
            
            // Guard against empty list to prevent off-by-one error
            if (total == 0)
            {
                _currentPage = 0;
                Threats.Clear();
                PaginationText = "No alerts";
                HasPrevious = false;
                HasNext = false;
                return;
            }
            
            int totalPages = (int)Math.Ceiling(total / (double)PageSize);
            _currentPage = Math.Clamp(_currentPage, 0, totalPages - 1);

            var page = list.Skip(_currentPage * PageSize).Take(PageSize).ToList();

            Threats.Clear();
            foreach (var t in page) Threats.Add(t);

            int from = _currentPage * PageSize + 1;
            int to = Math.Min((_currentPage + 1) * PageSize, total);
            PaginationText = total == 0 ? "No alerts" : $"Showing {from}–{to} of {total} alerts";
            HasPrevious = _currentPage > 0;
            HasNext = _currentPage < totalPages - 1;
        }

        partial void OnSearchQueryChanged(string value) => ApplyFilters();

        partial void OnSelectedSeverityIndexChanged(int value) { _currentPage = 0; ApplyFilters(); }
        partial void OnSelectedDateRangeIndexChanged(int value) { _currentPage = 0; ApplyFilters(); }

        [RelayCommand]
        private void PreviousPage() { if (_currentPage > 0) { _currentPage--; ApplyFilters(); } }

        [RelayCommand]
        private void NextPage() { _currentPage++; ApplyFilters(); }

        [RelayCommand]
        private void SyncLogs() { LoadThreats(); }

        [RelayCommand]
        private async Task QuarantineThreat(Threat threat)
        {
            if (threat == null || _disposed) return; // Check if disposed
            
            try { 
                await _monitorService.QuarantineFile(threat.Path); 
                threat.ActionTaken = "Quarantined"; // Tell the rest of the app (like Dashboard) it's handled
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"QuarantineThreat error: {ex.Message}");
            }
            
            lock (_threatsLock)
            {
                _allThreats.Remove(threat);
            }
            RefreshCounts();
            ApplyFilters();
        }

        [RelayCommand]
        private void IgnoreThreat(Threat threat)
        {
            if (threat == null || _disposed) return; // Check if disposed
            
            lock (_threatsLock)
            {
                _allThreats.Remove(threat);
            }
            RefreshCounts();
            ApplyFilters();
        }

        private void OnThreatDetected(Threat threat)
        {
            if (_disposed) return; // Check if disposed
            
            Application.Current.Dispatcher.Invoke(() =>
            {
                if (_disposed) return; // Check again in dispatcher
                
                lock (_threatsLock)
                {
                    // Remove existing entry for this path (if any) to ensure the latest event is shown
                    var existing = _allThreats.FirstOrDefault(t => string.Equals(t.Path, threat.Path, StringComparison.OrdinalIgnoreCase));
                    if (existing != null)
                        _allThreats.Remove(existing);

                    _allThreats.Insert(0, threat);

                    // Enforce cap
                    if (_allThreats.Count > MaxThreats)
                        _allThreats.RemoveAt(_allThreats.Count - 1);
                }
                // Always re-filter — an existing threat may have changed status (e.g. Quarantined from Dashboard)
                RefreshCounts();
                ApplyFilters();
            });
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            // Stop and dispose timer using named handler for proper unsubscription
            if (_refreshTimer != null)
            {
                _refreshTimer.Stop();
                if (_refreshTimerHandler != null)
                {
                    _refreshTimer.Tick -= _refreshTimerHandler;
                    _refreshTimerHandler = null;
                }
                _refreshTimer = null!;
            }

            // Unsubscribe from events
            if (_monitorService != null)
            {
                _monitorService.ThreatDetected -= OnThreatDetected;
            }
        }
    }
}
