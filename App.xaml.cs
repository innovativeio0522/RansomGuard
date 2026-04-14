using System.Windows;

namespace RansomGuard
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        protected override async void OnStartup(StartupEventArgs e)
        {
            // Global exception handling
            DispatcherUnhandledException += App_DispatcherUnhandledException;
            base.OnStartup(e);

            // Prevent app from closing when SplashWindow closes
            this.ShutdownMode = ShutdownMode.OnExplicitShutdown;

            var splash = new Views.SplashWindow();
            splash.Show();

            // Simulate loading steps for premium UX
            splash.UpdateStatus("Initializing Sentinel Engine...");
            await System.Threading.Tasks.Task.Delay(800);
            
            splash.UpdateStatus("Establishing IPC connections...");
            await System.Threading.Tasks.Task.Delay(800);

            splash.UpdateStatus("Loading Dashboard...");
            await System.Threading.Tasks.Task.Delay(800);

            var mainWindow = new MainWindow();
            this.MainWindow = mainWindow;
            mainWindow.Show();
            
            splash.Close();
            
            // Restore default shutdown behavior
            this.ShutdownMode = ShutdownMode.OnMainWindowClose;
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
