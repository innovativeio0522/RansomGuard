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
    public partial class MainViewModel : ViewModelBase
    {
        [ObservableProperty]
        private ViewModelBase _currentView;

        [ObservableProperty]
        private string _headerTitle = "Dashboard";

        [ObservableProperty]
        private string _headerStatus = "SYSTEM LIVE";

        [ObservableProperty]
        private string _searchPlaceholder = "Search system nodes...";
        
        [ObservableProperty]
        private bool _isServiceConnected = true;

        [ObservableProperty]
        private string _cpuUsageText = "CPU: --%";

        [ObservableProperty]
        private string _memoryUsageText = "MEM: --GB";

        private readonly ISystemMonitorService _monitorService;
        private readonly DispatcherTimer _statusBarTimer;

        // View instances to preserve state
        private readonly DashboardViewModel _dashboardVM;
        private readonly ThreatAlertsViewModel _threatAlertsVM;
        private readonly QuarantineViewModel _quarantineVM;
        private readonly ProcessMonitorViewModel _processMonitorVM;
        private readonly FileActivityViewModel _fileActivityVM;
        private readonly ReportsViewModel _reportsVM;
        private readonly SettingsViewModel _settingsVM;

        public MainViewModel()
        {
            // Initialize Services
            _monitorService = new ServicePipeClient();
            IsServiceConnected = _monitorService.IsConnected;
            _monitorService.ConnectionStatusChanged += (status) => IsServiceConnected = status;

            // Initialize ViewModels
            _dashboardVM = new DashboardViewModel(_monitorService);
            _threatAlertsVM = new ThreatAlertsViewModel(_monitorService);
            _quarantineVM = new QuarantineViewModel(_monitorService);
            _processMonitorVM = new ProcessMonitorViewModel(_monitorService);
            _fileActivityVM = new FileActivityViewModel(_monitorService);
            _reportsVM = new ReportsViewModel();
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
            catch { }
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
    }
}
