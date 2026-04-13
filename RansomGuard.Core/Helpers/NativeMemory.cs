using System.Runtime.InteropServices;

namespace RansomGuard.Core.Helpers
{
    /// <summary>
    /// Provides helper methods for native memory operations using Win32 API.
    /// Used to retrieve accurate system memory information on Windows platforms.
    /// </summary>
    public static class NativeMemory
    {
        /// <summary>
        /// Contains information about the current state of both physical and virtual memory.
        /// </summary>
        [StructLayout(LayoutKind.Sequential)]
        public class MEMORYSTATUSEX
        {
            /// <summary>Size of the structure in bytes.</summary>
            public uint dwLength = 64;
            /// <summary>Memory load percentage (0-100).</summary>
            public uint dwMemoryLoad;
            /// <summary>Total physical memory in bytes.</summary>
            public ulong ullTotalPhys;
            /// <summary>Available physical memory in bytes.</summary>
            public ulong ullAvailPhys;
            /// <summary>Total page file size in bytes.</summary>
            public ulong ullTotalPageFile;
            /// <summary>Available page file size in bytes.</summary>
            public ulong ullAvailPageFile;
            /// <summary>Total virtual memory in bytes.</summary>
            public ulong ullTotalVirtual;
            /// <summary>Available virtual memory in bytes.</summary>
            public ulong ullAvailVirtual;
            /// <summary>Available extended virtual memory in bytes.</summary>
            public ulong ullAvailExtendedVirtual;
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GlobalMemoryStatusEx([In, Out] MEMORYSTATUSEX lpBuffer);

        /// <summary>
        /// Gets the total physical memory installed on the system in megabytes.
        /// </summary>
        /// <returns>Total physical memory in MB, or 0 if the operation fails.</returns>
        public static double GetTotalPhysicalMemoryMb()
        {
            try
            {
                var memStatus = new MEMORYSTATUSEX();
                if (GlobalMemoryStatusEx(memStatus))
                {
                    return memStatus.ullTotalPhys / (1024.0 * 1024.0);
                }
            }
            catch { }
            return 0;
        }

        /// <summary>
        /// Gets the available physical memory on the system in megabytes.
        /// </summary>
        /// <returns>Available physical memory in MB, or 0 if the operation fails.</returns>
        public static double GetAvailablePhysicalMemoryMb()
        {
            try
            {
                var memStatus = new MEMORYSTATUSEX();
                if (GlobalMemoryStatusEx(memStatus))
                {
                    return memStatus.ullAvailPhys / (1024.0 * 1024.0);
                }
            }
            catch { }
            return 0;
        }

        /// <summary>
        /// Gets the currently used physical memory on the system in megabytes.
        /// </summary>
        /// <returns>Used physical memory in MB, or 0 if the operation fails.</returns>
        public static double GetUsedPhysicalMemoryMb()
        {
            try
            {
                var memStatus = new MEMORYSTATUSEX();
                if (GlobalMemoryStatusEx(memStatus))
                {
                    return (memStatus.ullTotalPhys - memStatus.ullAvailPhys) / (1024.0 * 1024.0);
                }
            }
            catch { }
            return 0;
        }

        /// <summary>
        /// Retrieves detailed memory status information from the system.
        /// </summary>
        /// <param name="memStatus">When this method returns, contains the memory status information.</param>
        /// <returns>true if the operation succeeded; otherwise, false.</returns>
        public static bool GetMemoryStatus(out MEMORYSTATUSEX memStatus)
        {
            memStatus = new MEMORYSTATUSEX();
            try
            {
                return GlobalMemoryStatusEx(memStatus);
            }
            catch
            {
                return false;
            }
        }
    }
}
