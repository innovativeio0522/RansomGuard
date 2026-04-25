using System;
using System.IO;

namespace RansomGuard.Core.Helpers
{
    /// <summary>
    /// Provides centralized path configuration for RansomGuard application directories.
    /// Automatically detects MSIX packaging and uses appropriate storage locations.
    /// </summary>
    public static class PathConfiguration
    {
        /// <summary>
        /// Detects if the application is running as an MSIX package.
        /// </summary>
        private static bool IsRunningAsMsix()
        {
            // MSIX apps have a specific environment variable set
            return !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("MSIX_PACKAGE_FAMILY_NAME"));
        }

        /// <summary>
        /// Gets the base directory for application data.
        /// Uses LocalApplicationData for MSIX packages, CommonApplicationData for traditional installs.
        /// </summary>
        private static readonly string BaseDirectory = GetBaseDirectory();

        private static string GetBaseDirectory()
        {
            var msixPackageName = Environment.GetEnvironmentVariable("MSIX_PACKAGE_FAMILY_NAME");
            var isMsix = !string.IsNullOrEmpty(msixPackageName);
            
            if (isMsix)
            {
                // MSIX apps should use LocalApplicationData which is writable
                var localAppData = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "RansomGuard"
                );
                
                // Log for debugging
                Console.WriteLine($"[PathConfiguration] Running as MSIX package: {msixPackageName}");
                Console.WriteLine($"[PathConfiguration] Using LocalApplicationData: {localAppData}");
                System.Diagnostics.Debug.WriteLine($"[PathConfiguration] Running as MSIX, using LocalApplicationData: {localAppData}");
                return localAppData;
            }
            else
            {
                // Traditional install uses ProgramData
                var programData = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                    "RansomGuard"
                );
                
                Console.WriteLine($"[PathConfiguration] Running as traditional app");
                Console.WriteLine($"[PathConfiguration] Using ProgramData: {programData}");
                System.Diagnostics.Debug.WriteLine($"[PathConfiguration] Running as traditional app, using ProgramData: {programData}");
                return programData;
            }
        }
        
        /// <summary>
        /// Gets the path to the quarantine directory where isolated suspicious files are stored.
        /// </summary>
        public static string QuarantinePath => Path.Combine(BaseDirectory, "Quarantine");
        
        /// <summary>
        /// Gets the path to the honey pot directory for bait files used to detect ransomware activity.
        /// </summary>
        public static string HoneyPotPath => Path.Combine(BaseDirectory, "HoneyPots");
        
        /// <summary>
        /// Gets the path to the log directory for application logs and diagnostic information.
        /// </summary>
        public static string LogPath => Path.Combine(BaseDirectory, "Logs");

        /// <summary>
        /// Gets the path to the activity log database.
        /// </summary>
        public static string ActivityLogDatabasePath => Path.Combine(BaseDirectory, "activity_log.db");
        
        /// <summary>
        /// Gets the configuration directory path.
        /// </summary>
        public static string GetConfigDirectory() => BaseDirectory;
        
        /// <summary>
        /// Ensures all required application directories exist, creating them if necessary.
        /// </summary>
        public static void EnsureDirectoriesExist()
        {
            try
            {
                Directory.CreateDirectory(QuarantinePath);
                Directory.CreateDirectory(HoneyPotPath);
                Directory.CreateDirectory(LogPath);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to create directories: {ex.Message}");
            }
        }
        
        static PathConfiguration()
        {
            EnsureDirectoriesExist();
        }
    }
}
