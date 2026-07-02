using System;
using System.Collections.Generic;
using Microsoft.Extensions.Logging;

namespace RansomGuard.Core.Services
{
    /// <summary>
    /// Extension methods for ILogger to support structured logging with correlation IDs.
    /// </summary>
    public static class LoggerExtensions
    {
        /// <summary>
        /// Logs a message with correlation ID and custom properties.
        /// </summary>
        public static void LogWithCorrelation(
            this ILogger logger,
            LogLevel level,
            string message,
            Exception? exception = null,
            params (string Key, object? Value)[] properties)
        {
            if (!logger.IsEnabled(level)) return;

            var state = new Dictionary<string, object?>
            {
                ["CorrelationId"] = StructuredLogger.CorrelationId,
                ["Message"] = message
            };

            foreach (var (key, value) in properties)
            {
                state[key] = value;
            }

            logger.Log(level, exception, message, state);
        }

        /// <summary>
        /// Logs an information message with correlation ID.
        /// </summary>
        public static void LogInformationWithCorrelation(
            this ILogger logger,
            string message,
            params (string Key, object? Value)[] properties)
        {
            logger.LogWithCorrelation(LogLevel.Information, message, null, properties);
        }

        /// <summary>
        /// Logs a warning message with correlation ID.
        /// </summary>
        public static void LogWarningWithCorrelation(
            this ILogger logger,
            string message,
            params (string Key, object? Value)[] properties)
        {
            logger.LogWithCorrelation(LogLevel.Warning, message, null, properties);
        }

        /// <summary>
        /// Logs an error message with correlation ID.
        /// </summary>
        public static void LogErrorWithCorrelation(
            this ILogger logger,
            string message,
            Exception? exception = null,
            params (string Key, object? Value)[] properties)
        {
            logger.LogWithCorrelation(LogLevel.Error, message, exception, properties);
        }

        /// <summary>
        /// Logs a critical error message with correlation ID.
        /// </summary>
        public static void LogCriticalWithCorrelation(
            this ILogger logger,
            string message,
            Exception? exception = null,
            params (string Key, object? Value)[] properties)
        {
            logger.LogWithCorrelation(LogLevel.Critical, message, exception, properties);
        }

        /// <summary>
        /// Logs a debug message with correlation ID.
        /// </summary>
        public static void LogDebugWithCorrelation(
            this ILogger logger,
            string message,
            params (string Key, object? Value)[] properties)
        {
            logger.LogWithCorrelation(LogLevel.Debug, message, null, properties);
        }

        /// <summary>
        /// Begins a timed operation scope that logs duration on disposal.
        /// </summary>
        public static IDisposable BeginTimedOperation(
            this ILogger logger,
            string operationName,
            LogLevel level = LogLevel.Information)
        {
            return new TimedOperationScope(logger, operationName, level);
        }

        private class TimedOperationScope : IDisposable
        {
            private readonly ILogger _logger;
            private readonly string _operationName;
            private readonly LogLevel _level;
            private readonly System.Diagnostics.Stopwatch _stopwatch;
            private readonly string _correlationId;
            private bool _disposed;

            public TimedOperationScope(ILogger logger, string operationName, LogLevel level)
            {
                _logger = logger;
                _operationName = operationName;
                _level = level;
                _correlationId = StructuredLogger.CorrelationId;
                _stopwatch = System.Diagnostics.Stopwatch.StartNew();

                _logger.LogWithCorrelation(
                    LogLevel.Debug,
                    $"Operation started: {operationName}",
                    null,
                    ("Operation", operationName));
            }

            public void Dispose()
            {
                if (_disposed) return;
                _disposed = true;

                _stopwatch.Stop();

                _logger.LogWithCorrelation(
                    _level,
                    $"Operation completed: {_operationName}",
                    null,
                    ("Operation", _operationName),
                    ("DurationMs", _stopwatch.ElapsedMilliseconds),
                    ("CorrelationId", _correlationId));
            }
        }
    }
}
