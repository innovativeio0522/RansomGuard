using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Windows.Input;

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
            // Initialize ViewModels
            _dashboardVM = new DashboardViewModel();
            _threatAlertsVM = new ThreatAlertsViewModel();
            _quarantineVM = new QuarantineViewModel();
            _processMonitorVM = new ProcessMonitorViewModel();
            _fileActivityVM = new FileActivityViewModel();
            _reportsVM = new ReportsViewModel();
            _settingsVM = new SettingsViewModel();

            // Set default view using the property to ensure notification
            CurrentView = _dashboardVM;
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
                    HeaderTitle = "System Quarantine";
                    HeaderStatus = "SECURED";
                    SearchPlaceholder = "Search quarantine...";
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
                    SearchPlaceholder = "Search files...";
                    break;
                case "Reports":
                    CurrentView = _reportsVM;
                    HeaderTitle = "Security Reports";
                    HeaderStatus = "IDLE";
                    SearchPlaceholder = "Search reports...";
                    break;
                case "Settings":
                    CurrentView = _settingsVM;
                    HeaderTitle = "System Configuration";
                    HeaderStatus = "AUTHENTICATED";
                    SearchPlaceholder = "Search parameters...";
                    break;
                default:
                    CurrentView = _dashboardVM;
                    break;
            }
        }
    }
}
