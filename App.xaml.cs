using System;
using System.Windows;
using RansomGuard.Services;
using RansomGuard.Core.Services;

namespace RansomGuard
{
    /// <summary>
    /// Interaction logic for App.xaml.
    /// - Always runs with ShutdownMode.OnExplicitShutdown so closing the window hides to tray.
    /// - Detects --startup argument (passed by Task Scheduler) to skip the splash and start silently.
    /// </summary>
    public partial class App : Application
    {
        private System.Threading.Mutex? _mutex;
        private bool _mutexOwned;
        private Services.TrayIconService? _tray;

        protected override async void OnStartup(StartupEventArgs e)
        {
            try
            {
                // ── Single-instance guard ────────────────────────────────────
                const string appName = "WinMaintenance_UI_Identity_Mutex";
                _mutex = new System.Threading.Mutex(true, appName, out bool createdNew);
                _mutexOwned = createdNew;

                if (!createdNew)
                {
                    MessageBox.Show("RansomGuard is already running.",
                        "RansomGuard", MessageBoxButton.OK, MessageBoxImage.Information);
                    Current.Shutdown();
                    return;
                }

                DispatcherUnhandledException += App_DispatcherUnhandledException;
                base.OnStartup(e);

                // App never quits when the main window is closed — we hide to tray instead.
                ShutdownMode = ShutdownMode.OnExplicitShutdown;

                // ── Detect launch mode ───────────────────────────────────────
                bool isStartup = Array.Exists(e.Args, a =>
                    a.Equals("--startup", StringComparison.OrdinalIgnoreCase));

                // ── Build tray icon (always) ─────────────────────────────────
                _tray = new Services.TrayIconService();

                // ── Engage Protection (Service + Watchdog) ──────────────────
                if (ConfigurationService.Instance.WatchdogEnabled)
                {
                    WatchdogManager.EnsureProtectionEngaged();
                }


                // ── Create main window ───────────────────────────────────────
                var mainWindow = new MainWindow();
                if (mainWindow == null)
                {
                    MessageBox.Show("Failed to create main window. Application will exit.",
                        "Fatal Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    Current.Shutdown();
                    return;
                }
                
                MainWindow = mainWindow;

                // Closing the window hides it to tray rather than exiting.
                mainWindow.Closing += (_, args) =>
                {
                    args.Cancel = true;
                    mainWindow.Hide();
                };

                // ── Notify user that protection is active (delayed and on UI thread) ──
                _ = ShowStartupNotificationAsync();

                if (isStartup)
                {
                    // Startup mode: stay hidden.
                }
                else
                {
                    // Normal launch: show splash then main window.
                    var splash = new Views.SplashWindow();
                    splash.Show();

                    splash.UpdateStatus("Initializing Sentinel Engine...");
                    await System.Threading.Tasks.Task.Delay(800);

                    splash.UpdateStatus("Establishing IPC connections...");
                    await System.Threading.Tasks.Task.Delay(800);

                    splash.UpdateStatus("Loading Dashboard...");
                    await System.Threading.Tasks.Task.Delay(800);

                    mainWindow.Show();
                    splash.Close();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Application failed to start: {ex.Message}",
                    "Fatal Error", MessageBoxButton.OK, MessageBoxImage.Error);
                Current.Shutdown();
            }
        }

        protected override void OnExit(ExitEventArgs e)
        {
            _tray?.Dispose();
            if (_mutexOwned) { _mutex?.ReleaseMutex(); _mutexOwned = false; }
            _mutex?.Dispose();
            base.OnExit(e);
        }

        private async System.Threading.Tasks.Task ShowStartupNotificationAsync()
        {
            await System.Threading.Tasks.Task.Delay(2000);
            _tray?.ShowBalloon(
                "Maintenance Active",
                "System resource monitoring is running in the background.",
                System.Windows.Forms.ToolTipIcon.Info);
        }

        private void App_DispatcherUnhandledException(object sender,
            System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
        {
            MessageBox.Show($"An error occurred: {e.Exception.Message}",
                "RansomGuard Error", MessageBoxButton.OK, MessageBoxImage.Error);
            e.Handled = true;
        }
    }
}
