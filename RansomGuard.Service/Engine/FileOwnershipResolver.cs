using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace RansomGuard.Service.Engine
{
    /// <summary>
    /// Uses the Windows Restart Manager API to identify which processes currently have a handle open to a specific file.
    /// This allows the SentinelEngine to attribute file system events to specific applications.
    /// </summary>
    internal static class FileOwnershipResolver
    {
        [StructLayout(LayoutKind.Sequential)]
        struct RM_UNIQUE_PROCESS
        {
            public int dwProcessId;
            public System.Runtime.InteropServices.ComTypes.FILETIME ProcessStartTime;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct RM_PROCESS_INFO
        {
            public RM_UNIQUE_PROCESS Process;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
            public string strAppName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
            public string strServiceShortName;
            public int ApplicationType;
            public int AppStatus;
            public int TSSessionId;
            [MarshalAs(UnmanagedType.Bool)]
            public bool bRestartable;
        }

        [DllImport("rstrtmgr.dll", CharSet = CharSet.Unicode)]
        static extern int RmStartSession(out uint pSessionHandle, int dwSessionFlags, string strSessionKey);

        [DllImport("rstrtmgr.dll")]
        static extern int RmEndSession(uint pSessionHandle);

        [DllImport("rstrtmgr.dll", CharSet = CharSet.Unicode)]
        static extern int RmRegisterResources(uint pSessionHandle, uint nFiles, string[] rgsFileNames,
            uint nApplications, RM_UNIQUE_PROCESS[] rgApplications, uint nServices, string[] rgsServiceNames);

        [DllImport("rstrtmgr.dll")]
        static extern int RmGetList(uint pSessionHandle, out uint pnProcInfoNeeded,
            ref uint pnProcInfo, [In, Out] RM_PROCESS_INFO[] rgAffectedApps, ref uint lpdwRebootReasons);

        private const int RmRebootReasonNone = 0;

        /// <summary>
        /// Attempts to find all processes that currently have a handle to the specified file.
        /// </summary>
        public static List<Process> GetProcessesUsingFile(string path)
        {
            var processes = new List<Process>();
            uint handle;
            string key = Guid.NewGuid().ToString();

            int res = RmStartSession(out handle, 0, key);
            if (res != 0) return processes;

            try
            {
                string[] resources = { path };
                res = RmRegisterResources(handle, (uint)resources.Length, resources, 0, Array.Empty<RM_UNIQUE_PROCESS>(), 0, Array.Empty<string>());

                if (res != 0) return processes;

                uint pnProcInfoNeeded = 0;
                uint pnProcInfo = 0;
                uint lpdwRebootReasons = RmRebootReasonNone;

                // First call to get the size needed
                res = RmGetList(handle, out pnProcInfoNeeded, ref pnProcInfo, Array.Empty<RM_PROCESS_INFO>(), ref lpdwRebootReasons);

                if (res == 234) // ERROR_MORE_DATA
                {
                    var processInfo = new RM_PROCESS_INFO[pnProcInfoNeeded];
                    pnProcInfo = pnProcInfoNeeded;
                    res = RmGetList(handle, out pnProcInfoNeeded, ref pnProcInfo, processInfo, ref lpdwRebootReasons);

                    if (res == 0)
                    {
                        for (int i = 0; i < pnProcInfo; i++)
                        {
                            try
                            {
                                var p = Process.GetProcessById(processInfo[i].Process.dwProcessId);
                                processes.Add(p);
                            }
                            catch { /* Process might have closed or exited */ }
                        }
                    }
                }
            }
            finally
            {
                RmEndSession(handle);
            }

            return processes;
        }
    }
}
