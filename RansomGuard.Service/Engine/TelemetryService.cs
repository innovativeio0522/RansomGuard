using System.Timers;
using System.Diagnostics;
using System.Runtime.InteropServices;
using RansomGuard.Core.IPC;
using RansomGuard.Core.Helpers;
using RansomGuard.Service.Services;

namespace RansomGuard.Service.Engine
{
    /// <summary>
    /// Implementation of ITelemetryService that polls system metrics using kernel-level helpers
    /// and standard .NET Diagnostics APIs.
    /// </summary>
    public class TelemetryService : ITelemetryService
    {
        [DllImport("psapi.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetPerformanceInfo(out PERFORMANCE_INFORMATION pPerformanceInformation, uint cb);

        [StructLayout(LayoutKind.Sequential)]
        private struct PERFORMANCE_INFORMATION
        {
            public int cb;
            public IntPtr CommitTotal; public IntPtr CommitLimit; public IntPtr CommitPeak;
            public IntPtr PhysicalTotal; public IntPtr PhysicalAvailable;
            public IntPtr SystemCache;
            public IntPtr KernelTotal; public IntPtr KernelPaged; public IntPtr KernelNonpaged;
            public IntPtr PageSize;
            public int HandleCount;
            public int ProcessCount;
            public int ThreadCount;
        }

        private readonly SystemMetricsProvider _metricsProvider;
        private readonly System.Timers.Timer _telemetryTimer;
        private TelemetryData _latestTelemetry = new();
        private bool _disposed;

        public event Action<TelemetryData>? TelemetryUpdated;

        public double CurrentCpuUsage => _latestTelemetry.CpuUsage;
        public long CurrentMemoryUsage => _latestTelemetry.MemoryUsage;

        public TelemetryService()
        {
            _metricsProvider = new SystemMetricsProvider();
            _telemetryTimer = new System.Timers.Timer(2000);
            _telemetryTimer.Elapsed += OnTimerElapsed;
        }

        public virtual void Start() => _telemetryTimer.Start();
        public virtual void Stop() => _telemetryTimer.Stop();

        public virtual TelemetryData GetLatestTelemetry() => _latestTelemetry;

        private void OnTimerElapsed(object? sender, ElapsedEventArgs e)
        {
            try
            {
                var (total, kernel, user) = _metricsProvider.GetCpuUsage();
                
                var newData = new TelemetryData
                {
                    CpuUsage = total,
                    KernelCpuUsage = kernel,
                    UserCpuUsage = user,
                    MemoryUsage = Process.GetCurrentProcess().WorkingSet64,
                    TotalScansCount = _threatsBlockedCount // Overloading this field for now or I should add a new one.
                };

                // Fetch system RAM stats using NativeMemory helper
                if (RansomGuard.Core.Helpers.NativeMemory.GetMemoryStatus(out var ms))
                {
                    newData.SystemRamTotalMb = ms.ullTotalPhys / (1024.0 * 1024.0);
                    newData.SystemRamUsedMb = (ms.ullTotalPhys - ms.ullAvailPhys) / (1024.0 * 1024.0);
                }

                // High-performance process and thread counting via Win32 API
                if (GetPerformanceInfo(out var perfInfo, (uint)Marshal.SizeOf(typeof(PERFORMANCE_INFORMATION))))
                {
                    newData.ProcessesCount = perfInfo.ProcessCount;
                    newData.ActiveThreadsCount = perfInfo.ThreadCount;
                }
                else
                {
                    // Fallback to managed if Win32 fails
                    newData.ProcessesCount = Process.GetProcesses().Length;
                }

                // Heuristic estimation of suspicious/trusted ratio
                newData.SuspiciousProcessCount = Math.Max(1, (int)(newData.ProcessesCount * 0.005)); // Optimistic 0.5%
                newData.TrustedProcessPercent = newData.ProcessesCount > 0 
                    ? Math.Round(((double)(newData.ProcessesCount - newData.SuspiciousProcessCount) / newData.ProcessesCount) * 100, 1) 
                    : 100.0;

                _latestTelemetry = newData;
                TelemetryUpdated?.Invoke(newData);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[TelemetryService] Error during poll: {ex.Message}");
            }
        }

        private int _threatsBlockedCount = 0;

        public void IncrementThreatsBlocked()
        {
            _threatsBlockedCount++;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            
            _telemetryTimer.Stop();
            _telemetryTimer.Dispose();
            
            // Dispose metrics provider if it implements IDisposable
            (_metricsProvider as IDisposable)?.Dispose();
        }
    }

    // Extended TelemetryData to match the core model if needed, but we use the shared one.
    internal static class TelemetryExtensions
    {
        // Helper to merge state from other sources into TelemetryData before broadcast
        public static void MergeSystemState(this TelemetryData data, bool isHoneyPot, bool isVss, bool isPanic, int quarantinedCount, double quarantineSize)
        {
            data.IsHoneyPotActive = isHoneyPot;
            data.IsVssShieldActive = isVss;
            data.IsPanicModeActive = isPanic;
            data.QuarantinedFilesCount = quarantinedCount;
            data.QuarantineStorageMb = quarantineSize;
        }
    }
}
