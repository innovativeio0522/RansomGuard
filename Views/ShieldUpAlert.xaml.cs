using System.Windows;

namespace RansomGuard.Views
{
    public partial class ShieldUpAlert : Window
    {
        public ShieldUpAlert()
        {
            InitializeComponent();
        }

        private void Restore_Click(object sender, RoutedEventArgs e)
        {
            // Close the alert and return to normal mode
            this.Close();
        }
    }
}
