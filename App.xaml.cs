using System.Windows;

namespace RansomGuard
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        private System.Threading.Mutex? _mutex;

        protected override async void OnStartup(StartupEventArgs e)
        {
            // Enforce Single Instance using a System Mutex
            const string appName = "RansomGuard_Dashboard_Mutex_GlobalLock";
            _mutex = new System.Threading.Mutex(true, appName, out bool createdNew);

            if (!createdNew)
            {
                MessageBox.Show("An instance of RansomGuard is already running.", "RansomGuard", MessageBoxButton.OK, MessageBoxImage.Information);
                Application.Current.Shutdown();
                return;
            }

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
