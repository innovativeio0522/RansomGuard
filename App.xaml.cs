using System.Windows;

namespace RansomGuard
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            // Global exception handling
            DispatcherUnhandledException += App_DispatcherUnhandledException;
            
            base.OnStartup(e);
        }

        private void App_DispatcherUnhandledException(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
        {
            // Log exception
            MessageBox.Show($"An error occurred: {e.Exception.Message}", 
                          "RansomGuard Error", 
                          MessageBoxButton.OK, 
                          MessageBoxImage.Error);
            
            e.Handled = true;
        }
    }
}
