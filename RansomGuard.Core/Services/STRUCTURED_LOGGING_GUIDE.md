# Structured Logging with Correlation IDs - Usage Guide

## Overview

RansomGuard now includes a comprehensive structured logging system with correlation IDs that allows tracking operations across components. This enables better debugging, monitoring, and troubleshooting of complex workflows.

## Key Features

- **Correlation IDs**: Unique identifiers that track operations across async boundaries
- **Structured Data**: Log entries include structured properties for easy querying
- **Operation Scopes**: Track operation duration and context automatically
- **Automatic Enrichment**: Thread IDs, timestamps, and caller information added automatically
- **Log Rotation**: Automatic daily rotation with 7-day retention
- **Multiple Log Levels**: Debug, Info, Warning, Error, Critical

## Quick Start

### 1. Initialize Logging (Done in App.xaml.cs)

```csharp
StructuredLogger.Initialize(PathConfiguration.LogPath);
```

### 2. Basic Logging

```csharp
// Simple log messages
StructuredLogger.LogInfo("User logged in successfully");
StructuredLogger.LogWarning("Configuration file not found, using defaults");
StructuredLogger.LogError("Failed to connect to service", exception);
```

### 3. Logging with Properties

```csharp
// Add structured properties for better querying
StructuredLogger.LogInfo("File quarantined",
    ("FilePath", path),
    ("ProcessName", processName),
    ("Entropy", entropy));
```

### 4. Operation Scopes with Correlation IDs

```csharp
// Track an operation across multiple components
using (StructuredLogger.BeginOperation("FileAnalysis"))
{
    StructuredLogger.LogInfo("Starting analysis");
    
    // All logs within this scope share the same correlation ID
    await AnalyzeFileAsync(path);
    
    StructuredLogger.LogInfo("Analysis completed");
}
```

### 5. Using ILogger with Extensions

```csharp
// Create a logger for your class
private readonly ILogger<MyClass> _logger = StructuredLogger.CreateLogger<MyClass>();

// Log with correlation ID automatically
_logger.LogInformationWithCorrelation("Processing started",
    ("ItemCount", items.Count));

// Timed operations
using (_logger.BeginTimedOperation("DataProcessing"))
{
    // Operation duration logged automatically on disposal
    ProcessData();
}
```

## Log Output Format

Logs are written in a structured format:

```
2026-05-15 14:23:45.123 [INF] [1234] [a1b2c3d4e5f6] [FileAnalysis] File quarantined successfully
  FilePath: "C:\Users\Test\Documents\suspicious.exe"
  ProcessName: "notepad"
  Entropy: 7.85
```

Fields:
- **Timestamp**: Precise millisecond timestamp
- **Level**: Log level (DBG, INF, WRN, ERR, FTL)
- **ThreadId**: Thread that generated the log
- **CorrelationId**: Unique ID tracking related operations
- **Operation**: Current operation name
- **Message**: Human-readable message
- **Properties**: Structured data as key-value pairs

## Correlation ID Flow

Correlation IDs automatically flow through:

1. **Async/Await**: Preserved across async boundaries using AsyncLocal
2. **Operation Scopes**: Nested operations inherit parent correlation ID
3. **Event Handlers**: Can be manually propagated via BeginOperation

### Example: Tracking a Threat Detection

```csharp
// In SentinelEngine
using (StructuredLogger.BeginOperation("ThreatDetection"))
{
    StructuredLogger.LogWarning("Suspicious file detected",
        ("FilePath", path),
        ("Entropy", entropy));
    
    // Correlation ID flows to quarantine service
    await _quarantine.QuarantineFile(path);
    
    // And to UI notification
    ThreatDetected?.Invoke(threat);
}

// In QuarantineService - same correlation ID
StructuredLogger.LogInfo("File moved to quarantine",
    ("OriginalPath", path),
    ("QuarantinePath", quarantinePath));

// In UI - same correlation ID
StructuredLogger.LogInfo("User notified of threat",
    ("ThreatId", threat.Id));
```

All three log entries share the same correlation ID, making it easy to trace the entire flow.

## Best Practices

### 1. Use Appropriate Log Levels

- **Debug**: Detailed diagnostic information (disabled in production)
- **Info**: General informational messages about application flow
- **Warning**: Potentially harmful situations that don't prevent operation
- **Error**: Error events that might still allow the application to continue
- **Critical**: Very severe errors that may cause application termination

### 2. Add Meaningful Properties

```csharp
// Good - structured properties
StructuredLogger.LogError("Quarantine failed", exception,
    ("FilePath", path),
    ("Reason", "Access denied"),
    ("RetryCount", retryCount));

// Bad - string interpolation loses structure
StructuredLogger.LogError($"Quarantine failed for {path}: Access denied");
```

### 3. Use Operation Scopes for Complex Workflows

```csharp
using (StructuredLogger.BeginOperation("MassEncryptionResponse"))
{
    StructuredLogger.LogCritical("Mass encryption detected",
        ("ProcessName", processName),
        ("FileCount", files.Count));
    
    await KillProcess(processId);
    await QuarantineFiles(files);
    
    StructuredLogger.LogInfo("Mitigation completed",
        ("QuarantinedCount", successCount),
        ("FailedCount", failCount));
}
```

### 4. Propagate Correlation IDs Across Boundaries

```csharp
// When calling external services or raising events
var correlationId = StructuredLogger.CorrelationId;

// Pass to event handler
ThreatDetected?.Invoke(threat, correlationId);

// In event handler
using (StructuredLogger.BeginOperation("ThreatHandling", correlationId))
{
    // Logs now share the same correlation ID
    HandleThreat(threat);
}
```

## Querying Logs

Logs can be queried using standard text tools or log analysis platforms:

### Find all logs for a specific correlation ID
```bash
grep "a1b2c3d4e5f6" ransomguard-2026-05-15.log
```

### Find all errors in the last hour
```bash
grep "\[ERR\]" ransomguard-2026-05-15.log | tail -100
```

### Find all quarantine operations
```bash
grep "QuarantineFile" ransomguard-2026-05-15.log
```

## Log File Location

Logs are stored in:
- **Windows**: `C:\ProgramData\RansomGuard\Logs\ransomguard-YYYY-MM-DD.log`

## Performance Considerations

- Structured logging is asynchronous and has minimal performance impact
- Log files are automatically rotated to prevent disk space issues
- Debug-level logs can be disabled in production builds
- Use operation scopes judiciously - they add overhead for tracking

## Migration from FileLogger

The old `FileLogger` is still available for backward compatibility, but new code should use `StructuredLogger`:

```csharp
// Old way
FileLogger.Log("sentinel_engine.log", $"[INFO] File analyzed: {path}");

// New way
StructuredLogger.LogInfo("File analyzed",
    ("FilePath", path),
    ("DurationMs", duration));
```

## Troubleshooting

### Logs not appearing
- Check that `StructuredLogger.Initialize()` is called at startup
- Verify log directory permissions
- Check disk space

### Correlation IDs not flowing
- Ensure you're using `BeginOperation()` to create scopes
- Verify async/await is used correctly (not Task.Run without context)
- Manually propagate IDs across event boundaries if needed

### Performance issues
- Reduce log verbosity in production
- Check log file rotation is working
- Consider using log aggregation tools for high-volume scenarios
