using System.Windows;
using System.Windows.Threading;
using RansomGuard.ViewModels;
using RansomGuard.Core.Models;
using RansomGuard.Core.Interfaces;
using System;
using System.Threading.Tasks;

namespace RansomGuard.Views
{
    public partial class ShieldUpAlert : Window
    {
        private DispatcherTimer _timer;
        private double _timeLeft = 5.0;
        private readonly Threat _threat;
        private readonly ISystemMonitorService _monitorService;
        private bool _mitigated = false;

        public ShieldUpAlert(Threat threat, ISystemMonitorService monitorService, bool isNetworkIsolated, bool isProcessTerminated, bool isQuarantined)
        {
            InitializeComponent();
            _threat = threat;
            _monitorService = monitorService;
            
            // Set visibility based on pending/actual actions
            NetworkDisabledText.Visibility = isNetworkIsolated ? Visibility.Visible : Visibility.Collapsed;
            ProcessTerminatedText.Visibility = isProcessTerminated ? Visibility.Visible : Visibility.Collapsed;
            QuarantinedText.Visibility = isQuarantined ? Visibility.Visible : Visibility.Collapsed;

            // Initialize timer
            _timer = new DispatcherTimer();
            _timer.Interval = TimeSpan.FromMilliseconds(100);
            _timer.Tick += Timer_Tick;
            _timer.Start();
        }

        private void Timer_Tick(object sender, EventArgs e)
        {
            _timeLeft -= 0.1;
            if (_timeLeft <= 0)
            {
                _timeLeft = 0;
                _timer.Stop();
                AutoMitigate();
            }

            CountdownProgress.Value = _timeLeft;
            TimerText.Text = $"{Math.Ceiling(_timeLeft)}s";
        }

        private async void AutoMitigate()
        {
            if (_mitigated) return;
            _mitigated = true;
            
            HeaderLabel.Text = "ATTACK BLOCKED";
            TimerText.Text = "BLOCKING...";
            
            await _monitorService.MitigateThreat(_threat.Id);
            
            await Task.Delay(2000); // Show blocked message for 2 seconds
            this.Close();
        }

        private void Yes_Click(object sender, RoutedEventArgs e)
        {
            _timer.Stop();
            this.Close();
        }

        private void No_Click(object sender, RoutedEventArgs e)
        {
            _timer.Stop();
            AutoMitigate();
        }

        private void Restore_Click(object sender, RoutedEventArgs e)
        {
            // Close the alert and return to normal mode
            this.Close();
        }

        private void ViewLogs_Click(object sender, RoutedEventArgs e)
        {
            // Try to find the main window and navigate to alerts
            var mainWindow = Application.Current.MainWindow;
            if (mainWindow != null)
            {
                if (mainWindow.DataContext is MainViewModel vm)
                {
                    vm.NavigateCommand.Execute("ThreatAlerts");
                }
                
                mainWindow.Show();
                if (mainWindow.WindowState == WindowState.Minimized)
                    mainWindow.WindowState = WindowState.Normal;
                mainWindow.Activate();
            }
            
            this.Close();
        }
    }
}
