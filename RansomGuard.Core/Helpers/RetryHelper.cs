using System;
using System.Threading.Tasks;
using RansomGuard.Core.Helpers;
using RansomGuard.Core.Constants;

namespace RansomGuard.Core.Helpers
{
    /// <summary>
    /// Utility for executing actions with retry logic and exponential backoff.
    /// Useful for transient failures like file locks or database busy states.
    /// </summary>
    public static class RetryHelper
    {
        private const int DefaultMaxRetries = 3;
        private const int DefaultInitialDelayMs = 100;

        /// <summary>
        /// Executes an async action with retry logic.
        /// </summary>
        public static async Task ExecuteAsync(Func<Task> action, int maxRetries = DefaultMaxRetries, int initialDelayMs = DefaultInitialDelayMs, Func<Exception, bool>? shouldRetry = null)
        {
            int retryCount = 0;
            int delay = initialDelayMs;

            while (true)
            {
                try
                {
                    await action();
                    return;
                }
                catch (Exception ex)
                {
                    retryCount++;
                    if (retryCount > maxRetries || (shouldRetry != null && !shouldRetry(ex)))
                    {
                        throw;
                    }

                    FileLogger.LogWarning(AppIdentifiers.SystemLogFile, $"[RetryHelper] Operation failed: {ex.Message}. Retrying ({retryCount}/{maxRetries}) in {delay}ms...");
                    await Task.Delay(delay);
                    delay *= 2; // Exponential backoff
                }
            }
        }

        /// <summary>
        /// Executes a synchronous action with retry logic.
        /// </summary>
        public static void Execute(Action action, int maxRetries = DefaultMaxRetries, int initialDelayMs = DefaultInitialDelayMs, Func<Exception, bool>? shouldRetry = null)
        {
            int retryCount = 0;
            int delay = initialDelayMs;

            while (true)
            {
                try
                {
                    action();
                    return;
                }
                catch (Exception ex)
                {
                    retryCount++;
                    if (retryCount > maxRetries || (shouldRetry != null && !shouldRetry(ex)))
                    {
                        throw;
                    }

                    FileLogger.LogWarning(AppIdentifiers.SystemLogFile, $"[RetryHelper] Operation failed: {ex.Message}. Retrying ({retryCount}/{maxRetries}) in {delay}ms...");
                    System.Threading.Thread.Sleep(delay);
                    delay *= 2; // Exponential backoff
                }
            }
        }
    }
}
