using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading;
using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Context;
using ILogger = Microsoft.Extensions.Logging.ILogger;

namespace RansomGuard.Core.Services
{
    /// <summary>
    /// Structured logging service with correlation IDs for tracking operations across components.
    /// Provides context-aware logging with automatic enrichment of log entries.
    /// </summary>
    public class StructuredLogger : IDisposable
    {
        private static readonly AsyncLocal<string?> _correlationId = new();
        private static readonly AsyncLocal<Stack<string>?> _operationStack = new();
        private static Serilog.Core.Logger? _serilogLogger;
        private static ILoggerFactory? _loggerFactory;
        private static bool _isInitialized;
        private static readonly object _initLock = new();

        /// <summary>
        /// Gets or sets the current correlation ID for the async context.
        /// </summary>
        public static string CorrelationId
        {
            get => _correlationId.Value ?? GenerateCorrelationId();
            set => _correlationId.Value = value;
        }

        /// <summary>
        /// Initializes the structured logger with Serilog configuration.
        /// </summary>
        public static void Initialize(string logDirectory)
        {
            lock (_initLock)
            {
                if (_isInitialized) return;

                try
                {
                    var logPath = System.IO.Path.Combine(logDirectory, "ransomguard-.log");

                    _serilogLogger = new LoggerConfiguration()
                        .MinimumLevel.Debug()
                        .Enrich.FromLogContext()
                        .Enrich.WithThreadId()
                        .Enrich.WithProperty("Application", "RansomGuard")
                        .WriteTo.File(
                            logPath,
                            rollingInterval: RollingInterval.Day,
                            retainedFileCountLimit: 7,
                            outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{Level:u3}] [{ThreadId}] [{CorrelationId}] [{Operation}] {Message:lj}{NewLine}{Exception}",
                            fileSizeLimitBytes: 10_485_760, // 10 MB
                            rollOnFileSizeLimit: true)
                        .CreateLogger();

                    _loggerFactory = LoggerFactory.Create(builder =>
                    {
                        builder.AddSerilog(_serilogLogger);
                    });

                    _isInitialized = true;

                    Log.Information("Structured logging initialized successfully");
                }
                catch (Exception ex)
                {
                    // Fallback to Debug output if initialization fails
                    Debug.WriteLine($"[StructuredLogger] Failed to initialize: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// Creates a logger for the specified category.
        /// </summary>
        public static ILogger CreateLogger(string categoryName)
        {
            if (!_isInitialized)
            {
                Initialize(RansomGuard.Core.Helpers.PathConfiguration.LogPath);
            }

            return _loggerFactory?.CreateLogger(categoryName) 
                ?? Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance;
        }

        /// <summary>
        /// Creates a logger for the specified type.
        /// </summary>
        public static ILogger<T> CreateLogger<T>()
        {
            if (!_isInitialized)
            {
                Initialize(RansomGuard.Core.Helpers.PathConfiguration.LogPath);
            }

            return _loggerFactory?.CreateLogger<T>() 
                ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<T>.Instance;
        }

        /// <summary>
        /// Generates a new correlation ID.
        /// </summary>
        public static string GenerateCorrelationId()
        {
            var newId = Guid.NewGuid().ToString("N")[..12]; // Short 12-char ID
            _correlationId.Value = newId;
            return newId;
        }

        /// <summary>
        /// Begins a new operation scope with a correlation ID.
        /// </summary>
        public static IDisposable BeginOperation(string operationName, string? correlationId = null)
        {
            if (!_isInitialized)
            {
                Initialize(RansomGuard.Core.Helpers.PathConfiguration.LogPath);
            }

            // Set or generate correlation ID
            if (!string.IsNullOrEmpty(correlationId))
            {
                CorrelationId = correlationId;
            }
            else if (string.IsNullOrEmpty(_correlationId.Value))
            {
                GenerateCorrelationId();
            }

            // Push operation onto stack
            var stack = _operationStack.Value ??= new Stack<string>();
            stack.Push(operationName);

            // Create Serilog context with correlation ID and operation
            var disposables = new List<IDisposable>
            {
                LogContext.PushProperty("CorrelationId", CorrelationId),
                LogContext.PushProperty("Operation", operationName)
            };

            return new OperationScope(disposables, () =>
            {
                // Pop operation from stack when disposed
                if (_operationStack.Value?.Count > 0)
                {
                    _operationStack.Value.Pop();
                }
            });
        }

        /// <summary>
        /// Logs a structured message with automatic correlation ID enrichment.
        /// </summary>
        public static void LogStructured(
            LogLevel level,
            string message,
            Exception? exception = null,
            [CallerMemberName] string? callerMember = null,
            [CallerFilePath] string? callerFile = null,
            [CallerLineNumber] int callerLine = 0,
            params (string Key, object? Value)[] properties)
        {
            if (!_isInitialized)
            {
                Initialize(RansomGuard.Core.Helpers.PathConfiguration.LogPath);
            }

            using (LogContext.PushProperty("CorrelationId", CorrelationId))
            using (LogContext.PushProperty("Caller", $"{System.IO.Path.GetFileNameWithoutExtension(callerFile)}.{callerMember}:{callerLine}"))
            {
                // Add custom properties
                var disposables = new List<IDisposable>();
                foreach (var (key, value) in properties)
                {
                    disposables.Add(LogContext.PushProperty(key, value));
                }

                try
                {
                    switch (level)
                    {
                        case LogLevel.Trace:
                            Log.Verbose(exception, message);
                            break;
                        case LogLevel.Debug:
                            Log.Debug(exception, message);
                            break;
                        case LogLevel.Information:
                            Log.Information(exception, message);
                            break;
                        case LogLevel.Warning:
                            Log.Warning(exception, message);
                            break;
                        case LogLevel.Error:
                            Log.Error(exception, message);
                            break;
                        case LogLevel.Critical:
                            Log.Fatal(exception, message);
                            break;
                    }
                }
                finally
                {
                    foreach (var disposable in disposables)
                    {
                        disposable.Dispose();
                    }
                }
            }
        }

        /// <summary>
        /// Convenience method for logging information.
        /// </summary>
        public static void LogInfo(string message, params (string Key, object? Value)[] properties)
        {
            LogStructured(LogLevel.Information, message, null, properties: properties);
        }

        /// <summary>
        /// Convenience method for logging warnings.
        /// </summary>
        public static void LogWarning(string message, params (string Key, object? Value)[] properties)
        {
            LogStructured(LogLevel.Warning, message, null, properties: properties);
        }

        /// <summary>
        /// Convenience method for logging errors.
        /// </summary>
        public static void LogError(string message, Exception? exception = null, params (string Key, object? Value)[] properties)
        {
            LogStructured(LogLevel.Error, message, exception, properties: properties);
        }

        /// <summary>
        /// Convenience method for logging critical errors.
        /// </summary>
        public static void LogCritical(string message, Exception? exception = null, params (string Key, object? Value)[] properties)
        {
            LogStructured(LogLevel.Critical, message, exception, properties: properties);
        }

        /// <summary>
        /// Convenience method for logging debug messages.
        /// </summary>
        public static void LogDebug(string message, params (string Key, object? Value)[] properties)
        {
            LogStructured(LogLevel.Debug, message, null, properties: properties);
        }

        public void Dispose()
        {
            _loggerFactory?.Dispose();
            _serilogLogger?.Dispose();
        }

        /// <summary>
        /// Operation scope that manages correlation context and cleanup.
        /// </summary>
        private class OperationScope : IDisposable
        {
            private readonly List<IDisposable> _disposables;
            private readonly Action _onDispose;
            private bool _disposed;

            public OperationScope(List<IDisposable> disposables, Action onDispose)
            {
                _disposables = disposables;
                _onDispose = onDispose;
            }

            public void Dispose()
            {
                if (_disposed) return;
                _disposed = true;

                foreach (var disposable in _disposables)
                {
                    disposable.Dispose();
                }

                _onDispose?.Invoke();
            }
        }
    }
}
