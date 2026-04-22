using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RansomGuard.Core.Models;
using RansomGuard.Core.Interfaces;
using RansomGuard.Core.Helpers;
using RansomGuard.Core.IPC;
using RansomGuard.Services;
using System.Collections.ObjectModel;
using System.Windows.Media;
using System.Linq;
using System.IO;
using System.Windows.Threading;

namespace RansomGuard.ViewModels
{
    public partial class QuarantineViewModel : ViewModelBase, IDisposable
    {
        private readonly ISystemMonitorService _monitorService;
        private readonly DispatcherTimer _refreshTimer;
        private bool _disposed;





        private List<QuarantineItemViewModel> _allItems = new();
        public ObservableCollection<QuarantineItemViewModel> QuarantinedItems { get; } = new();
        public ObservableCollection<Threat> TimelineEvents { get; } = new();

        [ObservableProperty]
        private bool _isAllSelected;

        [ObservableProperty]
        private int _currentPage = 1;

        [ObservableProperty]
        private int _totalPages = 1;

        [ObservableProperty]
        private int _pageSize = 10;

        [ObservableProperty]
        private string _paginationStatus = "Showing 0 of 0 quarantined items";

        [ObservableProperty]
        private bool _canNavigatePrevious;

        [ObservableProperty]
        private bool _canNavigateNext;

        [ObservableProperty]
        private int _totalItems;

        [ObservableProperty]
        private double _storageUsedMb;

        [ObservableProperty]
        private double _storagePercent;

        [ObservableProperty]
        private string _storageText = "0 MB / 5 GB Allocated";

        [ObservableProperty]
        private string _totalStorageText = "5 GB Allocated";

        public QuarantineViewModel(ISystemMonitorService monitorService)
        {
            _monitorService = monitorService;
            
            _monitorService.ThreatDetected += OnThreatDetected;

            LoadData();

            // Auto-refresh quarantine data every 5 seconds
            _refreshTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(5)
            };
            _refreshTimer.Tick += (s, e) => LoadData();
            _refreshTimer.Start();
        }

        private void LoadData()
        {
            StorageUsedMb = _monitorService.GetQuarantineStorageUsage();
            
            // Calculate storage percentage (assuming 5GB limit for now)
            StoragePercent = (StorageUsedMb / 5120.0) * 100.0;
            StorageText = $"{StorageUsedMb:F1} MB / 5 GB Allocated";
            TotalStorageText = "5 GB Allocated";

            var quarantinedFiles = _monitorService.GetQuarantinedFiles().ToList();
            _allItems.Clear();
            
            foreach (var filePath in quarantinedFiles)
            {
                var metaPath = filePath + ".metadata";
                string originalPath = "Unknown Path";
                DateTime timestamp = File.GetLastWriteTime(filePath);

                if (File.Exists(metaPath))
                {
                    try
                    {
                        var lines = File.ReadAllLines(metaPath);
                        foreach (var line in lines)
                        {
                            if (line.StartsWith("OriginalPath=")) originalPath = line.Substring("OriginalPath=".Length);
                        }
                    }
                    catch { }
                }

                _allItems.Add(new QuarantineItemViewModel(new Threat
                {
                    Name = Path.GetFileNameWithoutExtension(filePath),
                    Path = originalPath,
                    Description = filePath,
                    ProcessName = "Sentinel Quarantine",
                    Severity = ThreatSeverity.High,
                    ActionTaken = "Isolated",
                    Timestamp = timestamp
                }));
            }

            TotalItems = _allItems.Count;
            TotalPages = (int)Math.Ceiling((double)TotalItems / PageSize);
            if (CurrentPage > TotalPages && TotalPages > 0) CurrentPage = TotalPages;
            if (CurrentPage < 1) CurrentPage = Math.Max(1, TotalItems > 0 ? 1 : 0);

            UpdatePagedItems();

            // Load timeline events from actual quarantined items
            TimelineEvents.Clear();
            var recentQuarantined = _allItems.OrderByDescending(i => i.Threat.Timestamp).Take(10);
            foreach (var item in recentQuarantined) 
            {
                TimelineEvents.Add(item.Threat);
            }
        }

        private void UpdatePagedItems()
        {
            QuarantinedItems.Clear();
            var paged = _allItems
                .Skip((CurrentPage - 1) * PageSize)
                .Take(PageSize);

            foreach (var item in paged)
            {
                QuarantinedItems.Add(item);
            }

            CanNavigatePrevious = CurrentPage > 1;
            CanNavigateNext = CurrentPage < TotalPages;
            
            int start = TotalItems > 0 ? (CurrentPage - 1) * PageSize + 1 : 0;
            int end = Math.Min(CurrentPage * PageSize, TotalItems);
            PaginationStatus = $"Showing {start}-{end} of {TotalItems} quarantined items";
            
            // Sync IsAllSelected bit
            IsAllSelected = _allItems.Count > 0 && _allItems.All(i => i.IsSelected);
        }

        [RelayCommand]
        private void ToggleSelectAll()
        {
            foreach (var item in _allItems)
            {
                item.IsSelected = IsAllSelected;
            }
        }

        [RelayCommand]
        private void NavigateNext()
        {
            if (CurrentPage < TotalPages)
            {
                CurrentPage++;
                UpdatePagedItems();
            }
        }

        [RelayCommand]
        private void NavigatePrevious()
        {
            if (CurrentPage > 1)
            {
                CurrentPage--;
                UpdatePagedItems();
            }
        }

        [RelayCommand]
        private async Task RestoreSelected()
        {
            var selected = _allItems.Where(i => i.IsSelected).ToList();
            if (!selected.Any()) return;

            // OPTIMISTIC UI: Remove all selected from view instantly
            foreach (var item in selected)
            {
                QuarantinedItems.Remove(item);
                _allItems.Remove(item);
            }
            TotalItems = _allItems.Count;
            UpdatePagedItems();

            foreach (var item in selected)
            {
                try { await _monitorService.RestoreQuarantinedFile(item.Threat.Description); }
                catch { }
            }
        }

        [RelayCommand]
        private async Task PurgeSelected()
        {
            var selected = _allItems.Where(i => i.IsSelected).ToList();
            if (!selected.Any()) return;

            var result = System.Windows.MessageBox.Show(
                $"Are you sure you want to permanently delete {selected.Count} items?",
                "Confirm Purge",
                System.Windows.MessageBoxButton.YesNo,
                System.Windows.MessageBoxImage.Warning);

            if (result == System.Windows.MessageBoxResult.Yes)
            {
                // OPTIMISTIC UI: Remove all selected from view instantly
                foreach (var item in selected)
                {
                    QuarantinedItems.Remove(item);
                    _allItems.Remove(item);
                }
                TotalItems = _allItems.Count;
                UpdatePagedItems();

                foreach (var item in selected)
                {
                    try { await _monitorService.DeleteQuarantinedFile(item.Threat.Description); }
                    catch { }
                }
            }
        }

        [RelayCommand]
        private async Task ClearSafeFiles()
        {
            try
            {
                await _monitorService.ClearSafeFiles();
                LoadData();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"ClearSafeFiles request failed: {ex.Message}");
            }
        }



        private void OnThreatDetected(Threat threat)
        {
            if (threat.ActionTaken == "Quarantined" || threat.ActionTaken == "Isolated")
            {
                App.Current.Dispatcher.Invoke(() =>
                {
                    LoadData();
                });
            }
        }

        [RelayCommand]
        private async Task RestoreFile(QuarantineItemViewModel? item)
        {
            if (item == null) return;
            
            // OPTIMISTIC UI: Remove from view instantly
            QuarantinedItems.Remove(item);
            _allItems.Remove(item);
            TotalItems = _allItems.Count;
            UpdatePagedItems();

            try
            {
                await _monitorService.RestoreQuarantinedFile(item.Threat.Description);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to restore: {ex.Message}");
                // If it failed spectacularly, reload to be sure
                LoadData();
            }
        }

        [RelayCommand]
        private async Task DeleteFile(QuarantineItemViewModel? item)
        {
            if (item == null) return;

            // OPTIMISTIC UI: Remove from view instantly
            QuarantinedItems.Remove(item);
            _allItems.Remove(item);
            TotalItems = _allItems.Count;
            UpdatePagedItems();

            try
            {
                await _monitorService.DeleteQuarantinedFile(item.Threat.Description);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to delete: {ex.Message}");
                LoadData();
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            
            _monitorService.ThreatDetected -= OnThreatDetected;
            
            // Stop and dispose refresh timer
            if (_refreshTimer != null)
            {
                _refreshTimer.Stop();
            }
        }
    }
}
