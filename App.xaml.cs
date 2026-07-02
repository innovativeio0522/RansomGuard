using System.Windows;
using RansomGuard.Core.Constants;
using Microsoft.Extensions.DependencyInjection;
using RansomGuard.Services;
using RansomGuard.Core.Services;
using RansomGuard.Core.Helpers;
using RansomGuard.Core.Interfaces;
using RansomGuard.ViewModels;

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
        private IServiceProvider? _serviceProvider;

        /// <summary>
        /// Configures dependency injection services.
        /// </summary>
        private void ConfigureServices()
        {
            var services = new ServiceCollection();

            // Register core services
            var monitorService = new ServicePipeClient();
            services.AddSingleton<ISystemMonitorService>(monitorService);

            // Register UI services
            services.AddSingleton<TrayIconService>();
            
            // Register ViewModels
            services.AddTransient<MainViewModel>();
            services.AddTransient<DashboardViewModel>();
            services.AddTransient<SettingsViewModel>(sp => new SettingsViewModel(monitorService));
            services.AddTransient<ThreatAlertsViewModel>();
            services.AddTransient<ProcessMonitorViewModel>();
            services.AddTransient<QuarantineViewModel>();
            services.AddTransient<FileActivityViewModel>();
            services.AddTransient<ReportsViewModel>();

            // Register MainWindow
            services.AddTransient<MainWindow>();

            _serviceProvider = services.BuildServiceProvider();
        }

        /// <summary>
        /// Gets a service from the DI container.
        /// </summary>
        public T GetService<T>() where T : notnull
        {
            if (_serviceProvider == null)
                throw new InvalidOperationException("Service provider not initialized");
            
            return _serviceProvider.GetRequiredService<T>();
        }

        protected override async void OnStartup(StartupEventArgs e)
        {
            try
            {
                // ── Initialize Structured Logging ────────────────────────────
                RansomGuard.Core.Services.StructuredLogger.Initialize(
                    RansomGuard.Core.Helpers.PathConfiguration.LogPath);

                using (RansomGuard.Core.Services.StructuredLogger.BeginOperation("ApplicationStartup"))
                {
                    RansomGuard.Core.Services.StructuredLogger.LogInfo("RansomGuard application starting");

                    // ── Single-instance guard ────────────────────────────────────
                    _mutex = new System.Threading.Mutex(true, AppIdentifiers.UiMutexName, out bool createdNew);
                    _mutexOwned = createdNew;

                    if (!createdNew)
                    {
                        RansomGuard.Core.Services.StructuredLogger.LogWarning("Application already running - exiting");
                        MessageBox.Show("RG Core Essentials is already running.",
                            "RG Core Essentials", MessageBoxButton.OK, MessageBoxImage.Information);
                        Current.Shutdown();
                        return;
                    }

                    DispatcherUnhandledException += App_DispatcherUnhandledException;
                    base.OnStartup(e);

                    // ── Configure Dependency Injection ───────────────────────────
                    ConfigureServices();

                    // App never quits when the main window is closed — we hide to tray instead.
                    ShutdownMode = ShutdownMode.OnExplicitShutdown;

                    // ── Detect launch mode ───────────────────────────────────────
                    bool isStartup = Array.Exists(e.Args, a =>
                        a.Equals("--startup", StringComparison.OrdinalIgnoreCase));

                    RansomGuard.Core.Services.StructuredLogger.LogInfo("Launch mode detected",
                        ("IsStartup", isStartup));

                    // ── Build tray icon (from DI) ────────────────────────────────
                    _tray = GetService<TrayIconService>();

                    // ── Engage Protection (Service + Watchdog) ──────────────────
                    if (ConfigurationService.Instance.WatchdogEnabled)
                    {
                        RansomGuard.Core.Services.StructuredLogger.LogInfo("Engaging protection services");
                        WatchdogManager.EnsureProtectionEngaged();
                    }

                    // ── Create main window (from DI) ─────────────────────────────
                    var mainWindow = GetService<MainWindow>();
                    if (mainWindow == null)
                    {
                        RansomGuard.Core.Services.StructuredLogger.LogCritical("Failed to create main window");
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
#pragma warning disable CS4014 // Because this call is not awaited, execution continues before the call is completed
                    ShowStartupNotificationAsync().ContinueWith(task =>
                    {
                        if (task.IsFaulted && task.Exception != null)
                        {
                            FileLogger.LogError("app.log", "Startup notification failed", task.Exception);
                            RansomGuard.Core.Services.StructuredLogger.LogError(
                                "Startup notification failed", task.Exception);
                        }
                    }, TaskScheduler.Default);
#pragma warning restore CS4014

                    if (isStartup)
                    {
                        // Startup mode: stay hidden.
                        RansomGuard.Core.Services.StructuredLogger.LogInfo("Starting in silent mode");
                    }
                    else
                    {
                        // Normal launch: show splash then main window.
                        RansomGuard.Core.Services.StructuredLogger.LogInfo("Starting with splash screen");
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

                    RansomGuard.Core.Services.StructuredLogger.LogInfo("Application startup completed successfully");
                }
            }
            catch (Exception ex)
            {
                RansomGuard.Core.Services.StructuredLogger.LogCritical("Application startup failed", ex);
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
            
            // Dispose service provider
            if (_serviceProvider is IDisposable disposable)
            {
                disposable.Dispose();
            }
            
            base.OnExit(e);
        }

        private async System.Threading.Tasks.Task ShowStartupNotificationAsync()
        {
            await System.Threading.Tasks.Task.Delay(2000);
            _tray?.ShowBalloon(
                "RG Protection Active",
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
