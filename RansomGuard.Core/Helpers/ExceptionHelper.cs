using System;
using System.Diagnostics;

namespace RansomGuard.Core.Helpers
{
    /// <summary>
    /// Provides common exception handling patterns to reduce code duplication.
    /// Centralizes error logging and exception handling strategies.
    /// </summary>
    public static class ExceptionHelper
    {
        /// <summary>
        /// Executes an action with standardized exception handling and logging.
        /// </summary>
        /// <param name="action">The action to execute</param>
        /// <param name="context">Context description for logging (e.g., "QuarantineFile")</param>
        /// <param name="logFileName">Log file name (default: "error.log")</param>
        /// <param name="rethrow">Whether to rethrow the exception after logging</param>
        public static void ExecuteWithLogging(Action action, string context, string logFileName = "error.log", bool rethrow = false)
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                LogException(ex, context, logFileName);
                if (rethrow) throw;
            }
        }

        /// <summary>
        /// Executes an async action with standardized exception handling and logging.
        /// </summary>
        /// <param name="action">The async action to execute</param>
        /// <param name="context">Context description for logging</param>
        /// <param name="logFileName">Log file name (default: "error.log")</param>
        /// <param name="rethrow">Whether to rethrow the exception after logging</param>
        public static async System.Threading.Tasks.Task ExecuteWithLoggingAsync(
            Func<System.Threading.Tasks.Task> action, 
            string context, 
            string logFileName = "error.log", 
            bool rethrow = false)
        {
            try
            {
                await action().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                LogException(ex, context, logFileName);
                if (rethrow) throw;
            }
        }

        /// <summary>
        /// Executes a function with standardized exception handling and logging.
        /// Returns default(T) if an exception occurs and rethrow is false.
        /// </summary>
        /// <typeparam name="T">Return type</typeparam>
        /// <param name="func">The function to execute</param>
        /// <param name="context">Context description for logging</param>
        /// <param name="logFileName">Log file name (default: "error.log")</param>
        /// <param name="rethrow">Whether to rethrow the exception after logging</param>
        /// <returns>Function result or default(T) on error</returns>
        public static T? ExecuteWithLogging<T>(Func<T> func, string context, string logFileName = "error.log", bool rethrow = false)
        {
            try
            {
                return func();
            }
            catch (Exception ex)
            {
                LogException(ex, context, logFileName);
                if (rethrow) throw;
                return default;
            }
        }

        /// <summary>
        /// Logs an exception with context information.
        /// </summary>
        /// <param name="ex">The exception to log</param>
        /// <param name="context">Context description</param>
        /// <param name="logFileName">Log file name</param>
        public static void LogException(Exception ex, string context, string logFileName = "error.log")
        {
            string message = $"[{context}] {ex.GetType().Name}: {ex.Message}";
            FileLogger.LogError(logFileName, message, ex);
            Debug.WriteLine($"[ExceptionHelper] {message}");
        }

        /// <summary>
        /// Handles common IPC-related exceptions with appropriate logging.
        /// </summary>
        /// <param name="ex">The exception to handle</param>
        /// <param name="context">Context description</param>
        /// <returns>True if the exception was handled, false if it should be rethrown</returns>
        public static bool HandleIpcException(Exception ex, string context)
        {
            switch (ex)
            {
                case System.IO.IOException ioEx:
                    FileLogger.LogWarning("ipc.log", $"[{context}] IPC connection error: {ioEx.Message}");
                    return true; // Handled - connection issue

                case System.Text.Json.JsonException jsonEx:
                    FileLogger.LogError("ipc.log", $"[{context}] JSON serialization error", jsonEx);
                    return true; // Handled - malformed data

                case OperationCanceledException:
                    FileLogger.Log("ipc.log", $"[{context}] Operation cancelled");
                    return true; // Handled - cancellation is expected

                case ObjectDisposedException:
                    FileLogger.Log("ipc.log", $"[{context}] Object already disposed");
                    return true; // Handled - disposal during shutdown

                default:
                    FileLogger.LogError("ipc.log", $"[{context}] Unexpected error", ex);
                    return false; // Not handled - should be rethrown or investigated
            }
        }

        /// <summary>
        /// Handles common file operation exceptions with appropriate logging.
        /// </summary>
        /// <param name="ex">The exception to handle</param>
        /// <param name="context">Context description</param>
        /// <param name="filePath">The file path involved in the operation</param>
        /// <returns>True if the exception was handled, false if it should be rethrown</returns>
        public static bool HandleFileException(Exception ex, string context, string filePath)
        {
            switch (ex)
            {
                case System.IO.FileNotFoundException:
                    FileLogger.LogWarning("file_operations.log", $"[{context}] File not found: {filePath}");
                    return true; // Handled - file doesn't exist

                case System.IO.DirectoryNotFoundException:
                    FileLogger.LogWarning("file_operations.log", $"[{context}] Directory not found: {filePath}");
                    return true; // Handled - directory doesn't exist

                case UnauthorizedAccessException:
                    FileLogger.LogError("file_operations.log", $"[{context}] Access denied: {filePath}", ex);
                    return true; // Handled - permission issue

                case System.IO.IOException ioEx:
                    FileLogger.LogError("file_operations.log", $"[{context}] IO error for {filePath}", ioEx);
                    return true; // Handled - file in use or other IO issue

                default:
                    FileLogger.LogError("file_operations.log", $"[{context}] Unexpected error for {filePath}", ex);
                    return false; // Not handled - should be investigated
            }
        }
    }
}
