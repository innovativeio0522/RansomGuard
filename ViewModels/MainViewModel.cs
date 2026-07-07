using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Windows;
using System.Windows.Threading;
using RansomGuard.Services;
using RansomGuard.Core.Interfaces;
using RansomGuard.Core.Models;
using RansomGuard.Core.Helpers;
using RansomGuard.Core.Configuration;
using RansomGuard.Core.Constants;
using RansomGuard.Views;
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
        private string _systemName = Environment.MachineName;

        [ObservableProperty]
        private string _systemBadgeInitials = GetSystemBadgeInitials(Environment.MachineName);

        [ObservableProperty]
        private string _systemIdentityStatus = "Checking Service";
        
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
        private bool _isShowingMassEncryptionPrompt = false;

        private static string GetSystemBadgeInitials(string machineName)
        {
            var initials = new string(machineName
                .Where(char.IsLetterOrDigit)
                .Take(2)
                .Select(char.ToUpperInvariant)
                .ToArray());

            return string.IsNullOrWhiteSpace(initials) ? "PC" : initials;
        }

        // View instances to preserve state
        private readonly DashboardViewModel _dashboardVM = null!;
        private readonly ThreatAlertsViewModel _threatAlertsVM = null!;
        private readonly QuarantineViewModel _quarantineVM = null!;
        private readonly ProcessMonitorViewModel _processMonitorVM = null!;
        private readonly FileActivityViewModel _fileActivityVM = null!;
        private readonly ReportsViewModel _reportsVM = null!;
        private readonly SettingsViewModel _settingsVM = null!;

        public MainViewModel(
            ISystemMonitorService monitorService,
            DashboardViewModel dashboardVM,
            ThreatAlertsViewModel threatAlertsVM,
            QuarantineViewModel quarantineVM,
            ProcessMonitorViewModel processMonitorVM,
            FileActivityViewModel fileActivityVM,
            ReportsViewModel reportsVM,
            SettingsViewModel settingsVM)
        {
            try
            {
                // Initialize Services
                _monitorService = monitorService;
                SystemIdentityStatus = _monitorService.IsConnected ? "Protected System" : "Checking Service";
                
                // Start with IsServiceConnected = true to avoid showing banner during initial connection
                IsServiceConnected = true;
                
                _connectionStatusHandler = (status) =>
                {
                    // Marshal to UI thread to update binding
                    Application.Current.Dispatcher.Invoke(() => 
                    {
                        if (_disposed) return;
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
                        if (_disposed) return;
                        // Check if this is a mass encryption threat requiring user confirmation
                        if (threat.RequiresUserConfirmation && threat.Severity == ThreatSeverity.Critical)
                        {
                            // Show critical prompt with 5-second timeout
                            ShowMassEncryptionPrompt(threat);
                        }
                        else
                        {
                            // Normal threat notification
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
                    if (_disposed) return;
                    _gracePeriodExpired = true;
                    _connectionGraceTimer.Stop();
                    
                    // Now check actual connection status
                    IsServiceConnected = _monitorService.IsConnected;
                };
                _connectionGraceTimer.Start();

                // Initialize ViewModels
                _dashboardVM = dashboardVM;
                _dashboardVM.NavigationRequested = destination => Navigate(destination);
                _threatAlertsVM = threatAlertsVM;
                _quarantineVM = quarantineVM;
                _processMonitorVM = processMonitorVM;
                _fileActivityVM = fileActivityVM;
                _reportsVM = reportsVM;
                _settingsVM = settingsVM;

                // Set default view
                CurrentView = _dashboardVM;

                // Setup global status bar timer (3 second interval for background task)
                _statusBarTimer = new DispatcherTimer
                {
                    Interval = TimeSpan.FromSeconds(AppConstants.Timers.StatusBarUpdateSeconds)
                };
                _statusBarTimer.Tick += (s, e) =>
                {
                    if (_disposed) return;
                    UpdateStatusBarTelemetry();
                };
                _statusBarTimer.Start();

                UpdateStatusBarTelemetry();
            }
            catch (Exception ex)
            {
                FileLogger.LogError("ui_error.log", "FATAL INIT ERROR", ex);
            }
        }
        
        partial void OnSearchTextChanged(string value) => UpdateCurrentViewSearch();

        partial void OnIsServiceConnectedChanged(bool value)
        {
            SystemIdentityStatus = value ? "Protected System" : "Service Offline";
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
        private void OpenHelp(string url)
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = url ?? "https://github.com/innovativeio0522/RansomGuard",
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[MainViewModel] OpenHelp failed: {ex.Message}");
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

        /// <summary>
        /// Shows a critical mass encryption prompt with 5-second timeout.
        /// If user doesn't respond or clicks "No", automatically kills process and quarantines files.
        /// This executes REGARDLESS of AutoQuarantine settings.
        /// </summary>
        private async void ShowMassEncryptionPrompt(Threat threat)
        {
            if (_isShowingMassEncryptionPrompt) return;
            _isShowingMassEncryptionPrompt = true;

            try
            {
                FileLogger.Log("ui_critical.log", $"[CRITICAL] Mass encryption prompt shown for process: {threat.ProcessName} (PID: {threat.ProcessId})");

                // Show critical shield window with 5-second timeout
                ShieldAlertWindow? shieldWindow = null;
                bool? dialogResult = await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    shieldWindow = new ShieldAlertWindow(threat);
                    return shieldWindow.ShowDialog();
                });

                if (shieldWindow?.TimedOut == true)
                {
                    FileLogger.Log("ui_critical.log", $"[CRITICAL] Alert timed out for threat {threat.Id}. Awaiting service-side auto-mitigation.");
                    return;
                }

                bool shouldMitigate = dialogResult == true;
                FileLogger.Log("ui_critical.log", $"[CRITICAL] Alert closed. shouldMitigate: {shouldMitigate}");

                await _monitorService.HandleMassEncryptionResponse(
                    threat.Id,
                    shouldMitigate,
                    isUserInitiated: true,
                    threat.ProcessId,
                    threat.ProcessName,
                    threat.AffectedFiles);

                var confirmItem = new NotificationItem
                {
                    Title = shouldMitigate ? "Mass Encryption Mitigated" : "Mass Encryption Declined",
                    Message = shouldMitigate
                        ? $"Process terminated. {threat.AffectedFiles.Count} files quarantined."
                        : "Automatic mitigation was cancelled for this alert.",
                    Time = DateTime.Now.ToString("HH:mm:ss"),
                    Color = shouldMitigate ? "#ff5252" : "#ffb74d"
                };
                Notifications.Insert(0, confirmItem);
                if (Notifications.Count > 10) Notifications.RemoveAt(10);
                
                if (!IsNotificationFlyoutOpen)
                {
                    UnreadNotificationsCount++;
                }
            }
            catch (Exception ex)
            {
                FileLogger.LogError(AppIdentifiers.UiCriticalLogFile, "[CRITICAL] Error in ShowMassEncryptionPrompt", ex);
            }
            finally
            {
                _isShowingMassEncryptionPrompt = false;
            }
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
