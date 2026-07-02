using System.Windows.Controls;

namespace RansomGuard.Views
{
    public partial class SettingsView : UserControl
    {
        public SettingsView()
        {
            InitializeComponent();
            Loaded += SettingsView_Loaded;
        }

        private void SettingsView_Loaded(object sender, System.Windows.RoutedEventArgs e)
        {
            if (DataContext is ViewModels.SettingsViewModel vm)
            {
                // Unsubscribe first to avoid memory leaks if Loaded fires multiple times
                LanSharedSecretBox.PasswordChanged -= LanSharedSecretBox_PasswordChanged;
                
                LanSharedSecretBox.Password = vm.LanSharedSecret;
                
                LanSharedSecretBox.PasswordChanged += LanSharedSecretBox_PasswordChanged;
            }
        }

        private void LanSharedSecretBox_PasswordChanged(object sender, System.Windows.RoutedEventArgs e)
        {
            if (DataContext is ViewModels.SettingsViewModel vm)
            {
                vm.LanSharedSecret = LanSharedSecretBox.Password;
            }
        }
    }
}
