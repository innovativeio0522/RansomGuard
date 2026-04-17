using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using RansomGuard.Services;
using RansomGuard.Core.Interfaces;
using RansomGuard.Core.Models;

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
        private string _cpuUsageText = "CPU: --%";

        [ObservableProperty]
        private string _memoryUsageText = "MEM: --GB";

        private readonly ISystemMonitorService _monitorService = null!;
        private readonly DispatcherTimer _statusBarTimer = null!;
        private readonly DispatcherTimer _connectionGraceTimer = null!;
        private readonly Action<bool> _connectionStatusHandler = null!;
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
            }

            // Dispose child ViewModels
            (_dashboardVM as IDisposable)?.Dispose();
            (_threatAlertsVM as IDisposable)?.Dispose();
            (_quarantineVM as IDisposable)?.Dispose();
            (_processMonitorVM as IDisposable)?.Dispose();
            (_fileActivityVM as IDisposable)?.Dispose();

            // Dispose service if it implements IDisposable
            (_monitorService as IDisposable)?.Dispose();
        }
    }
}
