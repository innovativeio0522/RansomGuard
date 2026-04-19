using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Windows;
using System.Windows.Threading;
using RansomGuard.Services;
using RansomGuard.Core.Interfaces;
using RansomGuard.Core.Models;
using System.Collections.ObjectModel;
using System.Linq;

namespace RansomGuard.ViewModels
{
    public partial class MainViewModel : ViewModelBase, IDisposable
    {
        [ObservableProperty]
        private ViewModelBase _currentView = null!;

        [ObservableProperty]
        private string _headerTitle = "Dashboard";

        [ObservableProperty]
        private string _headerStatus = "SYSTEM LIVE";

        [ObservableProperty]
        private string _searchPlaceholder = "Search system nodes...";
        
        [ObservableProperty]
        private bool _isServiceConnected = true; // Start as true to give grace period

        [ObservableProperty]
        private bool _isSidebarCollapsed = false;

        [ObservableProperty]
        private string _cpuUsageText = "CPU: --%";

        [ObservableProperty]
        private string _memoryUsageText = "MEM: --GB";
        
        [ObservableProperty]
        private string _searchText = string.Empty;

        [ObservableProperty]
        private bool _isNotificationFlyoutOpen;

        [ObservableProperty]
        private int _unreadNotificationsCount;

        public ObservableCollection<NotificationItem> Notifications { get; } = new();

        private readonly ISystemMonitorService _monitorService = null!;
        private readonly DispatcherTimer _statusBarTimer = null!;
        private readonly DispatcherTimer _connectionGraceTimer = null!;
        private readonly Action<bool> _connectionStatusHandler = null!;
        private readonly Action<Threat> _threatDetectedHandler = null!;
        private bool _disposed;
        private bool _gracePeriodExpired = false;

        // View instances to preserve state
        private readonly DashboardViewModel _dashboardVM = null!;
        private readonly ThreatAlertsViewModel _threatAlertsVM = null!;
        private readonly QuarantineViewModel _quarantineVM = null!;
        private readonly ProcessMonitorViewModel _processMonitorVM = null!;
        private readonly FileActivityViewModel _fileActivityVM = null!;
        private readonly ReportsViewModel _reportsVM = null!;
        private readonly SettingsViewModel _settingsVM = null!;

        public MainViewModel()
        {
            try
            {
                // Initialize Services
                _monitorService = new ServicePipeClient();
                ((ServicePipeClient)_monitorService).Start(); // Begin IPC connection loop to the background service
                
                // Start with IsServiceConnected = true to avoid showing banner during initial connection
                IsServiceConnected = true;
                
                _connectionStatusHandler = (status) =>
                {
                    // Marshal to UI thread to update binding
                    Application.Current.Dispatcher.Invoke(() => 
                    {
                        // Only update if grace period expired or connection is successful
                        if (_gracePeriodExpired || status)
                        {
                            IsServiceConnected = status;
                        }
                    });
                };
                _monitorService.ConnectionStatusChanged += _connectionStatusHandler;

                _threatDetectedHandler = (threat) =>
                {
                    Application.Current.Dispatcher.Invoke(() => 
                    {
                        var item = new NotificationItem
                        {
                            Title = "Security Threat Detected",
                            Message = $"{threat.Name}: {threat.Path}",
                            Time = DateTime.Now.ToString("HH:mm:ss"),
                            Color = "#ff5252" // Tertiary / Alert Red
                        };
                        Notifications.Insert(0, item);
                        if (Notifications.Count > 10) Notifications.RemoveAt(10);
                        
                        if (!IsNotificationFlyoutOpen)
                        {
                            UnreadNotificationsCount++;
                        }
                    });
                };
                _monitorService.ThreatDetected += _threatDetectedHandler;

                // Grace period timer: After 6 seconds, allow showing offline status
                _connectionGraceTimer = new DispatcherTimer
                {
                    Interval = TimeSpan.FromSeconds(6)
                };
                _connectionGraceTimer.Tick += (s, e) =>
                {
                    _gracePeriodExpired = true;
                    _connectionGraceTimer.Stop();
                    
                    // Now check actual connection status
                    IsServiceConnected = _monitorService.IsConnected;
                };
                _connectionGraceTimer.Start();

                // Initialize ViewModels
                _dashboardVM = new DashboardViewModel(_monitorService);
                _dashboardVM.NavigationRequested = destination => Navigate(destination);
                _threatAlertsVM = new ThreatAlertsViewModel(_monitorService);
                _quarantineVM = new QuarantineViewModel(_monitorService);
                _processMonitorVM = new ProcessMonitorViewModel(_monitorService);
                _fileActivityVM = new FileActivityViewModel(_monitorService);
                _reportsVM = new ReportsViewModel(_monitorService);
                _settingsVM = new SettingsViewModel();

                // Set default view
                CurrentView = _dashboardVM;

                // Setup global status bar timer (3 second interval for background task)
                _statusBarTimer = new DispatcherTimer
                {
                    Interval = TimeSpan.FromSeconds(3)
                };
                _statusBarTimer.Tick += (s, e) => UpdateStatusBarTelemetry();
                _statusBarTimer.Start();

                UpdateStatusBarTelemetry();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"MainViewModel init error: {ex.Message}");
            }
        }
        
        partial void OnSearchTextChanged(string value) => UpdateCurrentViewSearch();

        private void UpdateStatusBarTelemetry()
        {
            try
            {
                double cpu = _monitorService.GetSystemCpuUsage();
                long memBytes = _monitorService.GetSystemMemoryUsage();
                double memGb = memBytes / (1024.0 * 1024.0 * 1024.0);

                CpuUsageText = $"CPU: {cpu:F1}%";
                MemoryUsageText = $"MEM: {memGb:F1}GB";
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"UpdateStatusBarTelemetry error: {ex.Message}");
            }
        }

        [RelayCommand]
        private void ToggleSidebar()
        {
            IsSidebarCollapsed = !IsSidebarCollapsed;
        }

        [RelayCommand]
        private void ToggleNotifications()
        {
            IsNotificationFlyoutOpen = !IsNotificationFlyoutOpen;
            if (IsNotificationFlyoutOpen)
            {
                UnreadNotificationsCount = 0;
            }
        }

        [RelayCommand]
        private void ClearNotifications()
        {
            Notifications.Clear();
            UnreadNotificationsCount = 0;
        }

        [RelayCommand]
        private void Navigate(string destination)
        {
            switch (destination)
            {
                case "Dashboard":
                    CurrentView = _dashboardVM;
                    HeaderTitle = "Dashboard";
                    HeaderStatus = "SYSTEM LIVE";
                    SearchPlaceholder = "Search system nodes...";
                    break;
                case "ThreatAlerts":
                    CurrentView = _threatAlertsVM;
                    HeaderTitle = "Threat Alerts";
                    HeaderStatus = "OPERATIONAL";
                    SearchPlaceholder = "Search events...";
                    break;
                case "Quarantine":
                    CurrentView = _quarantineVM;
                    HeaderTitle = "File Quarantine";
                    HeaderStatus = "SECURE";
                    SearchPlaceholder = "Search isolated files...";
                    break;
                case "ProcessMonitor":
                    CurrentView = _processMonitorVM;
                    HeaderTitle = "Process Monitor";
                    HeaderStatus = "MONITORING";
                    SearchPlaceholder = "Search processes...";
                    break;
                case "FileActivity":
                    CurrentView = _fileActivityVM;
                    HeaderTitle = "File Activity";
                    HeaderStatus = "LOGGING";
                    SearchPlaceholder = "Search file logs...";
                    break;
                case "Reports":
                    _reportsVM.Refresh();
                    CurrentView = _reportsVM;
                    HeaderTitle = "Security Reports";
                    HeaderStatus = "IDLE";
                    SearchPlaceholder = "Search reports...";
                    break;
                case "Settings":
                    CurrentView = _settingsVM;
                    HeaderTitle = "Global Settings";
                    HeaderStatus = "CONFIGURATION";
                    SearchPlaceholder = "Search settings...";
                    break;
            }
            
            UpdateCurrentViewSearch();
        }

        private void UpdateCurrentViewSearch()
        {
            if (CurrentView == null) return;
            
            // Using dynamic or pattern matching to pass search query to sub-viewmodels
            if (CurrentView is ProcessMonitorViewModel pmvm) pmvm.SearchQuery = SearchText;
            else if (CurrentView is FileActivityViewModel favm) favm.SearchQuery = SearchText;
            else if (CurrentView is ThreatAlertsViewModel tavm) tavm.SearchQuery = SearchText;
            else if (CurrentView is DashboardViewModel dvm) dvm.SearchQuery = SearchText;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            // Stop and dispose timers
            _statusBarTimer?.Stop();
            _connectionGraceTimer?.Stop();

            // Unsubscribe from events using stored delegate reference
            if (_monitorService != null)
            {
                _monitorService.ConnectionStatusChanged -= _connectionStatusHandler;
                _monitorService.ThreatDetected -= _threatDetectedHandler;
            }

            // Dispose child ViewModels
            (_dashboardVM as IDisposable)?.Dispose();
            (_threatAlertsVM as IDisposable)?.Dispose();
            (_quarantineVM as IDisposable)?.Dispose();
            (_processMonitorVM as IDisposable)?.Dispose();
            (_fileActivityVM as IDisposable)?.Dispose();
            (_reportsVM as IDisposable)?.Dispose();

            // Dispose service if it implements IDisposable
            (_monitorService as IDisposable)?.Dispose();
        }
    }

    public class NotificationItem
    {
        public string Title { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public string Time { get; set; } = string.Empty;
        public string Color { get; set; } = "#adc6ff";
    }
}
