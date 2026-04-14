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

        [ObservableProperty]
        private bool _isScanning;

        [ObservableProperty]
        private bool _isScanSummaryVisible;

        [ObservableProperty]
        private string _scanSummaryMessage = string.Empty;

        [ObservableProperty]
        private Brush _scanSummaryColor = Brushes.Transparent;

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
            _monitorService.ScanCompleted += OnScanCompleted;

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

            // Load timeline events
            var recentthreats = _monitorService.GetRecentThreats();
            TimelineEvents.Clear();
            foreach (var threat in recentthreats.Take(10)) 
            {
                TimelineEvents.Add(threat);
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

            foreach (var item in selected)
            {
                await RestoreFile(item);
            }
            LoadData();
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
                foreach (var item in selected)
                {
                    await DeleteFile(item);
                }
                LoadData();
            }
        }

        [RelayCommand]
        private async Task ClearSafeFiles()
        {
            IsScanning = true;
            try
            {
                await _monitorService.ClearSafeFiles();
                System.Diagnostics.Debug.WriteLine("Requested ClearSafeFiles from service.");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"ClearSafeFiles request failed: {ex.Message}");
            }
            
            IsScanning = false;
            LoadData();
        }

        [RelayCommand]
        private async Task ScanAllRepositories()
        {
            Console.WriteLine("ScanAllRepositories command triggered.");
            if (IsScanning) return;
            
            // Set scanning state and trigger refresh
            IsScanning = true;
            
            try
            {
                Console.WriteLine("Starting PerformQuickScan...");
                await _monitorService.PerformQuickScan();
                
                // We don't set IsScanning = false here anymore.
                // We wait for the ScanCompleted event from the service.
                // However, as a safety fallback, we'll auto-reset after 60 seconds.
                _ = Task.Delay(60000).ContinueWith(_ => {
                    if (IsScanning) IsScanning = false;
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Scan initiation failed: {ex.Message}");
                IsScanning = false;
            }
        }

        private void OnScanCompleted(ScanSummary summary)
        {
            App.Current.Dispatcher.Invoke(async () =>
            {
                Console.WriteLine($"UI received ScanCompleted signal. Checked: {summary.FilesChecked}, Threats: {summary.ThreatsFound}");
                IsScanning = false;
                LoadData();

                // Generate feedback message
                if (summary.ThreatsFound > 0)
                {
                    ScanSummaryMessage = $"SCAN COMPLETE: {summary.FilesChecked} files checked. {summary.ThreatsFound} THREATS IDENTIFIED AND ISOLATED.";
                    ScanSummaryColor = (Brush)App.Current.TryFindResource("TertiaryBrush") ?? Brushes.Red;
                }
                else
                {
                    ScanSummaryMessage = $"SCAN COMPLETE: {summary.FilesChecked} files checked. No threats found.";
                    ScanSummaryColor = (Brush)App.Current.TryFindResource("SecondaryBrush") ?? Brushes.Green;
                }

                IsScanSummaryVisible = true;

                // Auto-hide after 7 seconds
                await Task.Delay(7000);
                IsScanSummaryVisible = false;
            });
        }

        private void OnThreatDetected(Threat threat)
        {
            App.Current.Dispatcher.Invoke(() =>
            {
                TimelineEvents.Insert(0, threat);
                if (TimelineEvents.Count > 10) TimelineEvents.RemoveAt(10);
            });
        }

        [RelayCommand]
        private async Task RestoreFile(QuarantineItemViewModel? item)
        {
            if (item == null) return;
            var threat = item.Threat;

            try
            {
                // Delegate to service (Actual location is in threat.Description for quarantine files usually, 
                // but let's be sure we pass the correct path stored in the engine)
                string quarantinePath = threat.Description; 
                await _monitorService.RestoreQuarantinedFile(quarantinePath);
                System.Diagnostics.Debug.WriteLine($"Requested restoration of: {quarantinePath}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to request restore: {ex.Message}");
            }
            
            LoadData();
        }

        [RelayCommand]
        private async Task DeleteFile(QuarantineItemViewModel? item)
        {
            if (item == null) return;
            var threat = item.Threat;

            try
            {
                string quarantinePath = threat.Description;
                await _monitorService.DeleteQuarantinedFile(quarantinePath);
                System.Diagnostics.Debug.WriteLine($"Requested deletion of: {quarantinePath}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to request deletion: {ex.Message}");
            }
            
            LoadData();
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            
            _monitorService.ThreatDetected -= OnThreatDetected;
            _monitorService.ScanCompleted -= OnScanCompleted;
            
            // Stop and dispose refresh timer
            if (_refreshTimer != null)
            {
                _refreshTimer.Stop();
            }
        }
    }
}
