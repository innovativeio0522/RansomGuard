using System;
using System.IO;
using System.Linq;

namespace RansomGuard.Core.Helpers
{
    /// <summary>
    /// Centralized file logging utility with automatic log rotation.
    /// Prevents unbounded log file growth by rotating logs when they exceed size limits.
    /// </summary>
    public static class FileLogger
    {
        private const long MaxLogSizeBytes = 10 * 1024 * 1024; // 10 MB
        private const int MaxArchivedLogs = 5; // Keep last 5 archived logs
        private static readonly object _logLock = new object();

        /// <summary>
        /// Logs a message to the specified log file with automatic rotation.
        /// Falls back to Debug output if file logging fails.
        /// </summary>
        /// <param name="logFileName">Name of the log file (e.g., "ui_process.log")</param>
        /// <param name="message">Message to log</param>
        /// <param name="includeTimestamp">Whether to include timestamp (default: true)</param>
        public static void Log(string logFileName, string message, bool includeTimestamp = true)
        {
            try
            {
                string logDir = PathConfiguration.LogPath;
                if (!Directory.Exists(logDir))
                {
                    Directory.CreateDirectory(logDir);
                }

                string logPath = Path.Combine(logDir, logFileName);

                lock (_logLock)
                {
                    // Check if log rotation is needed
                    if (File.Exists(logPath))
                    {
                        var fileInfo = new FileInfo(logPath);
                        if (fileInfo.Length > MaxLogSizeBytes)
                        {
                            RotateLog(logPath, logDir, logFileName);
                        }
                    }

                    // Write log entry
                    string logEntry = includeTimestamp 
                        ? $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}: {message}"
                        : message;

                    using (var fs = new FileStream(logPath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite))
                    using (var sw = new StreamWriter(fs))
                    {
                        sw.WriteLine(logEntry);
                    }
                }
            }
            catch (Exception ex)
            {
                // Fallback to Debug output if file logging fails
                try
                {
                    System.Diagnostics.Debug.WriteLine($"[FileLogger] FAILED to log to {logFileName}: {ex.Message}");
                    System.Diagnostics.Debug.WriteLine($"[FileLogger] Original message: {message}");
                }
                catch
                {
                    // Last resort - silently fail
                    // Logging should never crash the application
                }
            }
        }

        /// <summary>
        /// Rotates the log file by renaming it with a timestamp and cleaning up old archives.
        /// </summary>
        private static void RotateLog(string logPath, string logDir, string logFileName)
        {
            try
            {
                // Create archive filename with timestamp
                string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                string archiveName = Path.GetFileNameWithoutExtension(logFileName);
                string archiveExt = Path.GetExtension(logFileName);
                string archivePath = Path.Combine(logDir, $"{archiveName}_{timestamp}{archiveExt}.old");

                // Rotate current log to archive
                File.Move(logPath, archivePath);

                // Clean up old archived logs (keep only last MaxArchivedLogs)
                var pattern = $"{archiveName}_*{archiveExt}.old";
                var oldLogs = Directory.GetFiles(logDir, pattern)
                    .OrderByDescending(f => f)
                    .Skip(MaxArchivedLogs)
                    .ToList();

                foreach (var oldLog in oldLogs)
                {
                    try
                    {
                        File.Delete(oldLog);
                    }
                    catch
                    {
                        // Ignore deletion errors
                    }
                }
            }
            catch
            {
                // If rotation fails, continue with current log file
            }
        }

        /// <summary>
        /// Logs a debug message (only in DEBUG builds).
        /// </summary>
        [System.Diagnostics.Conditional("DEBUG")]
        public static void LogDebug(string logFileName, string message)
        {
            Log(logFileName, $"[DEBUG] {message}");
        }

        /// <summary>
        /// Logs an error message.
        /// </summary>
        public static void LogError(string logFileName, string message, Exception? ex = null)
        {
            string errorMessage = ex != null 
                ? $"[ERROR] {message}\nException: {ex.Message}\nStackTrace: {ex.StackTrace}"
                : $"[ERROR] {message}";
            
            Log(logFileName, errorMessage);
        }

        /// <summary>
        /// Logs an informational message.
        /// </summary>
        public static void LogInfo(string logFileName, string message)
        {
            Log(logFileName, $"[INFO] {message}");
        }

        /// <summary>
        /// Logs a warning message.
        /// </summary>
        public static void LogWarning(string logFileName, string message)
        {
            Log(logFileName, $"[WARN] {message}");
        }
    }
}
