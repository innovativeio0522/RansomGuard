using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RansomGuard.Core.Models;
using RansomGuard.Core.Interfaces;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;

namespace RansomGuard.ViewModels
{
    public partial class ThreatAlertsViewModel : ViewModelBase, IDisposable
    {
        private readonly ISystemMonitorService _monitorService;
        private List<Threat> _allThreats = new();
        private bool _disposed;

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
        private string _paginationText = "No alerts";

        [ObservableProperty]
        private bool _hasPrevious = false;

        [ObservableProperty]
        private bool _hasNext = false;

        private int _currentPage = 0;
        private const int PageSize = 20;

        public ObservableCollection<Threat> Threats { get; } = new();

        public ThreatAlertsViewModel(ISystemMonitorService monitorService)
        {
            _monitorService = monitorService;
            LoadThreats();
            _monitorService.ThreatDetected += OnThreatDetected;
        }

        private void LoadThreats()
        {
            _allThreats = _monitorService.GetRecentThreats().ToList();
            RefreshCounts();
            ApplyFilters();
        }

        private void RefreshCounts()
        {
            CriticalThreatsCount = _allThreats.Count(t => t.Severity == ThreatSeverity.Critical);
            HighThreatsCount = _allThreats.Count(t => t.Severity == ThreatSeverity.High);
            MediumThreatsCount = _allThreats.Count(t => t.Severity == ThreatSeverity.Medium);
            LowThreatsCount = _allThreats.Count(t => t.Severity == ThreatSeverity.Low);
        }

        private void ApplyFilters()
        {
            var filtered = _allThreats.AsEnumerable();

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
            if (threat == null) return;
            try { await _monitorService.QuarantineFile(threat.Path); }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"QuarantineThreat error: {ex.Message}");
            }
            _allThreats.Remove(threat);
            RefreshCounts();
            ApplyFilters();
        }

        [RelayCommand]
        private void IgnoreThreat(Threat threat)
        {
            if (threat == null) return;
            _allThreats.Remove(threat);
            RefreshCounts();
            ApplyFilters();
        }

        private void OnThreatDetected(Threat threat)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                _allThreats.Insert(0, threat);
                RefreshCounts();
                ApplyFilters();
            });
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            // Unsubscribe from events
            if (_monitorService != null)
            {
                _monitorService.ThreatDetected -= OnThreatDetected;
            }
        }
    }
}
