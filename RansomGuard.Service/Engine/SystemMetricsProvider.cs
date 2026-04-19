using System;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace RansomGuard.Service.Engine
{
    /// <summary>
    /// Provides low-level system metrics using direct Windows Kernel APIs.
    /// This bypasses the often unreliable PerformanceCounter and WMI infrastructures.
    /// </summary>
    [SupportedOSPlatform("windows")]
    public class SystemMetricsProvider
    {
        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool GetSystemTimes(out FILETIME lpIdleTime, out FILETIME lpKernelTime, out FILETIME lpUserTime);

        [StructLayout(LayoutKind.Sequential)]
        struct FILETIME
        {
            public uint dwLowDateTime;
            public uint dwHighDateTime;

            public ulong ToTicks()
            {
                return ((ulong)dwHighDateTime << 32) | dwLowDateTime;
            }
        }

        private ulong _lastIdleTicks;
        private ulong _lastKernelTicks;
        private ulong _lastUserTicks;
        private DateTime _lastSampleTime;

        public SystemMetricsProvider()
        {
            // Initial sample to establish baseline
            Sample();
        }

        public (double Total, double Kernel, double User) GetCpuUsage()
        {
            if (!GetSystemTimes(out var idleTime, out var kernelTime, out var userTime))
                return (0, 0, 0);

            ulong currentIdleTicks = idleTime.ToTicks();
            ulong currentKernelTicks = kernelTime.ToTicks();
            ulong currentUserTicks = userTime.ToTicks();

            ulong idleDelta = currentIdleTicks - _lastIdleTicks;
            ulong kernelDelta = currentKernelTicks - _lastKernelTicks;
            ulong userDelta = currentUserTicks - _lastUserTicks;

            // Total time = Kernel + User (Note: Kernel includes Idle in Windows API)
            ulong totalDelta = kernelDelta + userDelta;

            if (totalDelta == 0) return (0, 0, 0);

            // Total CPU % = (Total - Idle) / Total
            double totalUsage = 100.0 * (totalDelta - idleDelta) / totalDelta;
            
            // Kernel % = (Kernel - Idle) / Total
            double kernelUsage = 100.0 * (kernelDelta - idleDelta) / totalDelta;
            
            // User % = User / Total
            double userUsage = 100.0 * userDelta / totalDelta;

            // Update baseline
            _lastIdleTicks = currentIdleTicks;
            _lastKernelTicks = currentKernelTicks;
            _lastUserTicks = currentUserTicks;
            _lastSampleTime = DateTime.Now;

            return (
                Math.Clamp(totalUsage, 0, 100),
                Math.Clamp(kernelUsage, 0, 100),
                Math.Clamp(userUsage, 0, 100)
            );
        }

        private void Sample()
        {
            if (GetSystemTimes(out var idleTime, out var kernelTime, out var userTime))
            {
                _lastIdleTicks = idleTime.ToTicks();
                _lastKernelTicks = kernelTime.ToTicks();
                _lastUserTicks = userTime.ToTicks();
                _lastSampleTime = DateTime.Now;
            }
        }
    }
}
