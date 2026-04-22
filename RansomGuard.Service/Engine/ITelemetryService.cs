using System;
using RansomGuard.Core.IPC;

namespace RansomGuard.Service.Engine
{
    /// <summary>
    /// Defines a service that periodically polls system metrics and broadcasts telemetry updates.
    /// Internal to the service engine.
    /// </summary>
    public interface ITelemetryService : IDisposable
    {
        /// <summary>
        /// Raised when new telemetry data has been sampled.
        /// </summary>
        event Action<TelemetryData> TelemetryUpdated;

        /// <summary>
        /// Starts the periodic telemetry polling.
        /// </summary>
        void Start();

        /// <summary>
        /// Stops the telemetry polling.
        /// </summary>
        void Stop();

        /// <summary>
        /// Returns the most recently cached telemetry data.
        /// </summary>
        TelemetryData GetLatestTelemetry();

        // Metrics for individual lookups
        double CurrentCpuUsage { get; }
        long CurrentMemoryUsage { get; }
    }
}
