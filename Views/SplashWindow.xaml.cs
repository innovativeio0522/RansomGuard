using System.Windows;

namespace RansomGuard.Views
{
    public partial class SplashWindow : Window
    {
        public SplashWindow()
        {
            InitializeComponent();
        }

        public void UpdateStatus(string message)
        {
            StatusText.Text = message;
        }
    }
}
