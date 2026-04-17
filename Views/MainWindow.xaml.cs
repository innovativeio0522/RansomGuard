using System.Windows;
using RansomGuard.ViewModels;

namespace RansomGuard
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
            DataContext = new MainViewModel();
            ApplyScreenPercentageSize();
        }

        private void ApplyScreenPercentageSize()
        {
            var screen = SystemParameters.WorkArea; // Excludes taskbar
            Width  = screen.Width  * 0.63;
            Height = screen.Height * 0.72;
            Left   = screen.Left + (screen.Width  - Width)  / 2;
            Top    = screen.Top  + (screen.Height - Height) / 2;
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
