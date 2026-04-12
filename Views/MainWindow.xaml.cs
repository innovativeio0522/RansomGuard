using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using RansomGuard.Views;

namespace RansomGuard
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private Button[] _navButtons;
        
        public MainWindow()
        {
            InitializeComponent();
            
            // Load Dashboard by default
            MainContentArea.Content = new DashboardView();
            
            // Store navigation buttons for styling
            _navButtons = new[] { BtnDashboard, BtnThreatAlerts, BtnQuarantine, BtnProcessMonitor, BtnFileActivity, BtnReports, BtnSettings };
        }

        private void NavButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.Tag is string viewName)
            {
                // Update button styles
                UpdateNavButtonStyles(button);
                
                // Navigate to selected view
                NavigateToView(viewName);
            }
        }

        private void UpdateNavButtonStyles(Button activeButton)
        {
            foreach (var btn in _navButtons)
            {
                if (btn == activeButton)
                {
                    // Active state
                    btn.Background = (Brush)FindResource("SurfaceContainerHighestBrush")!;
                    btn.BorderThickness = new Thickness(0, 0, 3, 0);
                    btn.BorderBrush = (Brush)FindResource("PrimaryBrush")!;
                    
                    ApplyNavElementStyles(btn, true);
                }
                else
                {
                    // Inactive state
                    btn.Background = Brushes.Transparent;
                    btn.BorderThickness = new Thickness(0);
                    
                    ApplyNavElementStyles(btn, false);
                }
            }
        }

        private void ApplyNavElementStyles(DependencyObject parent, bool isActive)
        {
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                
                if (child is Path p)
                {
                    p.Opacity = isActive ? 1.0 : 0.5;
                    p.Fill = (Brush)FindResource(isActive ? "PrimaryBrush" : "OnSurfaceVariantBrush")!;
                }
                else if (child is TextBlock tb)
                {
                    tb.Foreground = (Brush)FindResource(isActive ? "PrimaryBrush" : "OnSurfaceVariantBrush")!;
                    tb.FontWeight = isActive ? FontWeights.Bold : FontWeights.Medium;
                    tb.Opacity = isActive ? 1.0 : 0.7;
                }
                else
                {
                    ApplyNavElementStyles(child, isActive);
                }
            }
        }

        private void NavigateToView(string viewName)
        {
            UserControl? selectedView = viewName switch
            {
                "Dashboard" => new DashboardView(),
                "ThreatAlerts" => new ThreatAlertsView(),
                "Quarantine" => new QuarantineView(),
                "ProcessMonitor" => new ProcessMonitorView(),
                "FileActivity" => new FileActivityView(),
                "Reports" => new ReportsView(),
                "Settings" => new SettingsView(),
                _ => new DashboardView()
            };

            // Update Header dynamically
            UpdateHeaderContext(viewName);

            MainContentArea.Content = selectedView;
        }

        private void UpdateHeaderContext(string viewName)
        {
            if (TxtHeaderTitle == null) return;

            switch (viewName)
            {
                case "Settings":
                    TxtHeaderTitle.Text = "System Configuration";
                    TxtHeaderStatus.Text = "SECURED";
                    TxtHeaderStatus.Foreground = (Brush)FindResource("SecondaryBrush")!;
                    BrdHeaderStatus.Background = new SolidColorBrush(Color.FromArgb(26, 78, 222, 163)); // 10% opacity
                    TxtSearchPlaceholder.Text = "Search parameters...";
                    break;
                case "Dashboard":
                    TxtHeaderTitle.Text = "Dashboard";
                    TxtHeaderStatus.Text = "SYSTEM LIVE";
                    TxtHeaderStatus.Foreground = (Brush)FindResource("SecondaryBrush")!;
                    BrdHeaderStatus.Background = new SolidColorBrush(Color.FromArgb(26, 78, 222, 163));
                    TxtSearchPlaceholder.Text = "Search system nodes...";
                    break;
                default:
                    TxtHeaderTitle.Text = viewName switch {
                        "ThreatAlerts" => "Threat Alerts",
                        "Quarantine" => "System Quarantine",
                        "ProcessMonitor" => "Process Monitor",
                        "FileActivity" => "File Activity",
                        "Reports" => "Security Reports",
                        _ => viewName
                    };
                    TxtHeaderStatus.Text = "OPERATIONAL";
                    TxtHeaderStatus.Foreground = (Brush)FindResource("OnSurfaceVariantBrush")!;
                    BrdHeaderStatus.Background = new SolidColorBrush(Color.FromArgb(26, 194, 198, 214));
                    TxtSearchPlaceholder.Text = "Search events...";
                    break;
            }
        }

        private void MinButton_Click(object sender, RoutedEventArgs e)
        {
            this.WindowState = WindowState.Minimized;
        }

        private void MaxButton_Click(object sender, RoutedEventArgs e)
        {
            if (this.WindowState == WindowState.Maximized)
                this.WindowState = WindowState.Normal;
            else
                this.WindowState = WindowState.Maximized;
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }
    }
}
