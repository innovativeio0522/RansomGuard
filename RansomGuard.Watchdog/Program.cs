using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.ServiceProcess;
using System.Text.Json;
using System.Threading;

namespace RansomGuard.Watchdog
{
    class Program
    {
        [DllImport("kernel32.dll")]
        static extern IntPtr GetConsoleWindow();

        [DllImport("user32.dll")]
        static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        const int SW_HIDE = 0;
        const string ServiceName = "RansomGuardSentinel";
        private static readonly string ConfigPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "RansomGuard", "config.json");

        static void Main(string[] args)
        {
            // Stealth mode: Hide the console window
            var handle = GetConsoleWindow();
            ShowWindow(handle, SW_HIDE);

            Console.WriteLine("RansomGuard Watchdog Active.");

            while (true)
            {
                try
                {
                    // Check if user has disabled the Watchdog via Settings — exit if so.
                    if (!IsWatchdogEnabled())
                    {
                        Console.WriteLine("Watchdog disabled by user. Exiting.");
                        Environment.Exit(0);
                        return;
                    }

                    CheckServiceStatus();
                    CheckUIStatus();
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Watchdog error: {ex.Message}");
                }

                Thread.Sleep(3000); // Check every 3 seconds
            }
        }

        /// <summary>
        /// Reads WatchdogEnabled from config.json. Defaults to true if file is missing or unreadable.
        /// </summary>
        static bool IsWatchdogEnabled()
        {
            try
            {
                if (!File.Exists(ConfigPath)) return true;
                string json = File.ReadAllText(ConfigPath);
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("WatchdogEnabled", out var prop))
                    return prop.GetBoolean();
                return true; // Default: enabled
            }
            catch { return true; }
        }

        static void CheckUIStatus()
        {
            var processes = Process.GetProcessesByName("RansomGuard");
            if (processes.Length == 0)
            {
                try
                {
                    string appDir = AppDomain.CurrentDomain.BaseDirectory;
                    string appPath = Path.Combine(appDir, "RansomGuard.exe");

                    // Development fallback path
                    if (!File.Exists(appPath))
                    {
                        var devPath = Path.GetFullPath(Path.Combine(
                            appDir, @"..\..\..\..\bin\Debug\net8.0-windows\RansomGuard.exe"));
                        if (File.Exists(devPath))
                            appPath = devPath;
                    }

                    if (File.Exists(appPath))
                    {
                        ProcessStartInfo psi = new ProcessStartInfo(appPath, "--startup")
                        {
                            UseShellExecute = true
                        };
                        Process.Start(psi);
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"UI Watchdog error: {ex.Message}");
                }
            }
        }

        static void CheckServiceStatus()
        {
            using (ServiceController sc = new ServiceController(ServiceName))
            {
                try
                {
                    if (sc.Status == ServiceControllerStatus.Stopped || sc.Status == ServiceControllerStatus.StopPending)
                    {
                        Thread.Sleep(1000);
                        sc.Refresh();

                        if (sc.Status == ServiceControllerStatus.Stopped)
                        {
                            sc.Start();
                            sc.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(10));
                        }
                    }
                }
                catch (InvalidOperationException)
                {
                    // Service might not be installed yet
                }
            }
        }
    }
}
