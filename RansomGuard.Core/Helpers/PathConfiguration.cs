using System;
using System.IO;

namespace RansomGuard.Core.Helpers
{
    /// <summary>
    /// Provides centralized path configuration for RansomGuard application directories.
    /// All paths use the CommonApplicationData folder for proper Windows conventions.
    /// </summary>
    public static class PathConfiguration
    {
        private static readonly string BaseDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "RansomGuard"
        );
        
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
