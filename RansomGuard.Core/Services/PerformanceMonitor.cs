using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Linq;
using System.Threading;

namespace RansomGuard.Core.Services
{
    /// <summary>
    /// Lightweight performance monitoring service using .NET Metrics API (System.Diagnostics.Metrics).
    /// Tracks operation durations, counters, and provides a ring-buffer of recent samples
    /// for the dashboard without requiring an external metrics backend.
    /// </summary>
    public sealed class PerformanceMonitor : IDisposable
    {
        // ── Singleton ──────────────────────────────────────────────────────────
        private static readonly Lazy<PerformanceMonitor> _instance =
            new(() => new PerformanceMonitor(), LazyThreadSafetyMode.ExecutionAndPublication);

        public static PerformanceMonitor Instance => _instance.Value;

        // ── .NET Meter (OpenTelemetry-compatible) ──────────────────────────────
        private readonly Meter _meter;

        // Counters
        private readonly Counter<long> _filesAnalyzed;
        private readonly Counter<long> _threatsDetected;
        private readonly Counter<long> _filesQuarantined;
        private readonly Counter<long> _eventsDropped;
        private readonly Counter<long> _massEncryptionAlerts;

        // Histograms (duration in ms)
        private readonly Histogram<double> _analysisLatency;
        private readonly Histogram<double> _processAttributionLatency;
        private readonly Histogram<double> _entropyCalcLatency;
        private readonly Histogram<double> _ipcWriteLatency;

        // Gauges (observable)
        private long _eventQueueDepth;
        private long _activeAnalysisCount;
        private long _watcherCount;

        // ── In-process ring buffer for dashboard ───────────────────────────────
        private const int RingBufferSize = 500;
        private readonly ConcurrentQueue<OperationSample> _recentSamples = new();

        // ── Snapshot for fast reads ────────────────────────────────────────────
        private volatile PerformanceSnapshot _snapshot = new();
        private long _totalFilesAnalyzed;
        private long _totalThreatsDetected;
        private long _totalFilesQuarantined;
        private long _totalEventsDropped;
        private long _totalMassEncryptionAlerts;

        // ── Timing accumulators (for rolling averages) ─────────────────────────
        private readonly RollingAverage _analysisAvg = new(100);
        private readonly RollingAverage _attributionAvg = new(100);
        private readonly RollingAverage _entropyAvg = new(100);
        private readonly RollingAverage _ipcAvg = new(100);

        private bool _disposed;

        // ── Constructor ────────────────────────────────────────────────────────
        private PerformanceMonitor()
        {
            _meter = new Meter("RansomGuard.Service", "1.0.0");

            // Counters
            _filesAnalyzed = _meter.CreateCounter<long>(
                "ransomguard.files.analyzed",
                unit: "{files}",
                description: "Total number of file events analyzed");

            _threatsDetected = _meter.CreateCounter<long>(
                "ransomguard.threats.detected",
                unit: "{threats}",
                description: "Total number of threats detected");

            _filesQuarantined = _meter.CreateCounter<long>(
                "ransomguard.files.quarantined",
                unit: "{files}",
                description: "Total number of files quarantined");

            _eventsDropped = _meter.CreateCounter<long>(
                "ransomguard.events.dropped",
                unit: "{events}",
                description: "File events dropped due to full channel");

            _massEncryptionAlerts = _meter.CreateCounter<long>(
                "ransomguard.mass_encryption.alerts",
                unit: "{alerts}",
                description: "Number of mass encryption alerts triggered");

            // Histograms
            _analysisLatency = _meter.CreateHistogram<double>(
                "ransomguard.analysis.duration",
                unit: "ms",
                description: "Duration of file analysis operations");

            _processAttributionLatency = _meter.CreateHistogram<double>(
                "ransomguard.process_attribution.duration",
                unit: "ms",
                description: "Duration of process attribution operations");

            _entropyCalcLatency = _meter.CreateHistogram<double>(
                "ransomguard.entropy.duration",
                unit: "ms",
                description: "Duration of Shannon entropy calculations");

            _ipcWriteLatency = _meter.CreateHistogram<double>(
                "ransomguard.ipc.write_duration",
                unit: "ms",
                description: "Duration of IPC pipe write operations");

            // Observable gauges
            _meter.CreateObservableGauge(
                "ransomguard.event_queue.depth",
                () => (double)Interlocked.Read(ref _eventQueueDepth),
                unit: "{events}",
                description: "Current depth of the file event processing queue");

            _meter.CreateObservableGauge(
                "ransomguard.analysis.active_count",
                () => (double)Interlocked.Read(ref _activeAnalysisCount),
                unit: "{analyses}",
                description: "Number of file analyses currently in progress");

            _meter.CreateObservableGauge(
                "ransomguard.watchers.count",
                () => (double)Interlocked.Read(ref _watcherCount),
                unit: "{watchers}",
                description: "Number of active FileSystemWatcher instances");
        }

        // ── Public recording API ───────────────────────────────────────────────

        /// <summary>Records a completed file analysis with its duration.</summary>
        public void RecordFileAnalysis(double durationMs, bool wasSuspicious, bool wasDropped = false)
        {
            _filesAnalyzed.Add(1);
            Interlocked.Increment(ref _totalFilesAnalyzed);
            _analysisLatency.Record(durationMs);
            _analysisAvg.Add(durationMs);

            if (wasDropped)
            {
                _eventsDropped.Add(1);
                Interlocked.Increment(ref _totalEventsDropped);
            }

            AddSample(new OperationSample("FileAnalysis", durationMs, wasSuspicious));
            RefreshSnapshot();
        }

        /// <summary>Records a process attribution operation.</summary>
        public void RecordProcessAttribution(double durationMs, bool timedOut)
        {
            _processAttributionLatency.Record(durationMs);
            _attributionAvg.Add(durationMs);
            AddSample(new OperationSample("ProcessAttribution", durationMs, timedOut));
        }

        /// <summary>Records a Shannon entropy calculation.</summary>
        public void RecordEntropyCalculation(double durationMs, double entropyValue)
        {
            _entropyCalcLatency.Record(durationMs);
            _entropyAvg.Add(durationMs);
            AddSample(new OperationSample("EntropyCalc", durationMs, entropyValue > 7.5));
            RefreshSnapshot();
        }

        /// <summary>Records an IPC pipe write operation.</summary>
        public void RecordIpcWrite(double durationMs, bool succeeded)
        {
            _ipcWriteLatency.Record(durationMs);
            _ipcAvg.Add(durationMs);
            RefreshSnapshot();
        }

        /// <summary>Records a detected threat.</summary>
        public void RecordThreatDetected()
        {
            _threatsDetected.Add(1);
            Interlocked.Increment(ref _totalThreatsDetected);
            RefreshSnapshot();
        }

        /// <summary>Records a quarantined file.</summary>
        public void RecordFileQuarantined()
        {
            _filesQuarantined.Add(1);
            Interlocked.Increment(ref _totalFilesQuarantined);
            RefreshSnapshot();
        }

        /// <summary>Records a mass encryption alert.</summary>
        public void RecordMassEncryptionAlert()
        {
            _massEncryptionAlerts.Add(1);
            Interlocked.Increment(ref _totalMassEncryptionAlerts);
            RefreshSnapshot();
        }

        // ── Gauge setters ──────────────────────────────────────────────────────

        public void SetEventQueueDepth(long depth)
        {
            Interlocked.Exchange(ref _eventQueueDepth, depth);
            RefreshSnapshot();
        }

        public void SetActiveAnalysisCount(long count)
        {
            Interlocked.Exchange(ref _activeAnalysisCount, count);
            RefreshSnapshot();
        }

        public void SetWatcherCount(long count)
        {
            Interlocked.Exchange(ref _watcherCount, count);
            RefreshSnapshot();
        }

        // ── Timing helpers ─────────────────────────────────────────────────────

        /// <summary>
        /// Starts a timed operation scope. Dispose the returned handle to record the duration.
        /// </summary>
        public TimedOperation MeasureFileAnalysis(bool wasSuspicious = false) =>
            new(durationMs => RecordFileAnalysis(durationMs, wasSuspicious));

        public TimedOperation MeasureProcessAttribution(bool timedOut = false) =>
            new(durationMs => RecordProcessAttribution(durationMs, timedOut));

        public TimedOperation MeasureEntropyCalculation(double entropyValue = 0) =>
            new(durationMs => RecordEntropyCalculation(durationMs, entropyValue));

        public TimedOperation MeasureIpcWrite(bool succeeded = true) =>
            new(durationMs => RecordIpcWrite(durationMs, succeeded));

        // ── Snapshot / dashboard ───────────────────────────────────────────────

        /// <summary>Returns a point-in-time snapshot of all performance metrics.</summary>
        public PerformanceSnapshot GetSnapshot() => _snapshot;

        /// <summary>Returns the most recent operation samples (up to <paramref name="count"/>).</summary>
        public IReadOnlyList<OperationSample> GetRecentSamples(int count = 50)
        {
            return _recentSamples.TakeLast(Math.Min(count, RingBufferSize)).ToList();
        }

        // ── Private helpers ────────────────────────────────────────────────────

        private void AddSample(OperationSample sample)
        {
            _recentSamples.Enqueue(sample);
            // Trim ring buffer
            while (_recentSamples.Count > RingBufferSize)
                _recentSamples.TryDequeue(out _);
        }

        private void RefreshSnapshot()
        {
            _snapshot = new PerformanceSnapshot
            {
                TotalFilesAnalyzed = Interlocked.Read(ref _totalFilesAnalyzed),
                TotalThreatsDetected = Interlocked.Read(ref _totalThreatsDetected),
                TotalFilesQuarantined = Interlocked.Read(ref _totalFilesQuarantined),
                TotalEventsDropped = Interlocked.Read(ref _totalEventsDropped),
                TotalMassEncryptionAlerts = Interlocked.Read(ref _totalMassEncryptionAlerts),
                AvgAnalysisMs = _analysisAvg.Average,
                AvgProcessAttributionMs = _attributionAvg.Average,
                AvgEntropyCalcMs = _entropyAvg.Average,
                AvgIpcWriteMs = _ipcAvg.Average,
                P95AnalysisMs = _analysisAvg.Percentile(95),
                EventQueueDepth = Interlocked.Read(ref _eventQueueDepth),
                ActiveAnalysisCount = Interlocked.Read(ref _activeAnalysisCount),
                WatcherCount = Interlocked.Read(ref _watcherCount),
                SnapshotTime = DateTime.UtcNow
            };
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _meter.Dispose();
        }

        // ── Nested types ───────────────────────────────────────────────────────

        /// <summary>RAII timing scope — records duration on Dispose.</summary>
        public readonly struct TimedOperation : IDisposable
        {
            private readonly long _startTicks;
            private readonly Action<double> _onComplete;

            internal TimedOperation(Action<double> onComplete)
            {
                _startTicks = Stopwatch.GetTimestamp();
                _onComplete = onComplete;
            }

            public void Dispose()
            {
                double ms = (Stopwatch.GetTimestamp() - _startTicks) * 1000.0 / Stopwatch.Frequency;
                _onComplete?.Invoke(ms);
            }
        }

        /// <summary>Rolling average over the last N samples with P95 support.</summary>
        private sealed class RollingAverage
        {
            private readonly double[] _buffer;
            private int _head;
            private int _count;
            private readonly object _lock = new();

            public RollingAverage(int capacity) => _buffer = new double[capacity];

            public void Add(double value)
            {
                lock (_lock)
                {
                    _buffer[_head] = value;
                    _head = (_head + 1) % _buffer.Length;
                    if (_count < _buffer.Length) _count++;
                }
            }

            public double Average
            {
                get
                {
                    lock (_lock)
                    {
                        if (_count == 0) return 0;
                        double sum = 0;
                        for (int i = 0; i < _count; i++) sum += _buffer[i];
                        return Math.Round(sum / _count, 2);
                    }
                }
            }

            public double Percentile(int p)
            {
                lock (_lock)
                {
                    if (_count == 0) return 0;
                    var sorted = _buffer.Take(_count).OrderBy(x => x).ToArray();
                    int idx = (int)Math.Ceiling(p / 100.0 * sorted.Length) - 1;
                    return Math.Round(sorted[Math.Max(0, idx)], 2);
                }
            }
        }
    }

    // ── Public data types ──────────────────────────────────────────────────────

    /// <summary>Point-in-time snapshot of all performance counters.</summary>
    public sealed class PerformanceSnapshot
    {
        public long TotalFilesAnalyzed { get; init; }
        public long TotalThreatsDetected { get; init; }
        public long TotalFilesQuarantined { get; init; }
        public long TotalEventsDropped { get; init; }
        public long TotalMassEncryptionAlerts { get; init; }

        public double AvgAnalysisMs { get; init; }
        public double AvgProcessAttributionMs { get; init; }
        public double AvgEntropyCalcMs { get; init; }
        public double AvgIpcWriteMs { get; init; }
        public double P95AnalysisMs { get; init; }

        public long EventQueueDepth { get; init; }
        public long ActiveAnalysisCount { get; init; }
        public long WatcherCount { get; init; }

        public DateTime SnapshotTime { get; init; } = DateTime.UtcNow;

        public override string ToString() =>
            $"[Perf] Analyzed={TotalFilesAnalyzed} Threats={TotalThreatsDetected} " +
            $"Quarantined={TotalFilesQuarantined} Dropped={TotalEventsDropped} " +
            $"AvgAnalysis={AvgAnalysisMs:F1}ms P95={P95AnalysisMs:F1}ms " +
            $"AvgEntropy={AvgEntropyCalcMs:F1}ms AvgIpc={AvgIpcWriteMs:F1}ms " +
            $"Queue={EventQueueDepth} Active={ActiveAnalysisCount}";
    }

    /// <summary>A single timed operation sample stored in the ring buffer.</summary>
    public sealed class OperationSample
    {
        public string Operation { get; }
        public double DurationMs { get; }
        public bool WasAnomalous { get; }
        public DateTime Timestamp { get; }

        public OperationSample(string operation, double durationMs, bool wasAnomalous)
        {
            Operation = operation;
            DurationMs = durationMs;
            WasAnomalous = wasAnomalous;
            Timestamp = DateTime.UtcNow;
        }
    }
}
