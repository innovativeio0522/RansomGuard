using System;
using System.Management;
using System.Runtime.Versioning;
using RansomGuard.Core.Helpers;
using RansomGuard.Core.Constants;

namespace RansomGuard.Service.Engine
{
    [SupportedOSPlatform("windows")]
    public interface IEtwMonitorService : IDisposable
    {
        event Action<EtwFileEvent>? FileEventDetected;
        event Action<EtwProcessEvent>? ProcessStarted;
        void Start();
        void Stop();
    }

    public record EtwFileEvent(string Path, string Action, int ProcessId, string ProcessName);
    public record EtwProcessEvent(int ProcessId, string ProcessName, string CommandLine, int ParentId);

    [SupportedOSPlatform("windows")]
    public class EtwMonitorService : IEtwMonitorService
    {
        private ManagementEventWatcher? _processWatcher;
        private bool _isRunning;

#pragma warning disable CS0067
        public event Action<EtwFileEvent>? FileEventDetected;
#pragma warning restore CS0067
        public event Action<EtwProcessEvent>? ProcessStarted;

        public void Start()
        {
            if (_isRunning) return;

            try
            {
                _processWatcher = new ManagementEventWatcher(
                    new WqlEventQuery("SELECT * FROM Win32_ProcessStartTrace"));
                _processWatcher.EventArrived += OnProcessStarted;
                _processWatcher.Start();

                _isRunning = true;
            }
            catch (Exception ex)
            {
                FileLogger.LogError(AppIdentifiers.EtwMonitorLogFile, "Failed to start process monitor", ex);
                _isRunning = false;
            }
        }

        private void OnProcessStarted(object sender, EventArrivedEventArgs e)
        {
            try
            {
                string processName = e.NewEvent.Properties["ProcessName"]?.Value?.ToString() ?? "Unknown";
                int processId = Convert.ToInt32(e.NewEvent.Properties["ProcessID"]?.Value ?? 0);
                int parentId = Convert.ToInt32(e.NewEvent.Properties["ParentProcessID"]?.Value ?? 0);

                ProcessStarted?.Invoke(new EtwProcessEvent(
                    processId,
                    processName,
                    string.Empty,
                    parentId));
            }
            catch (Exception ex)
            {
                FileLogger.LogError(AppIdentifiers.EtwMonitorLogFile, "Process monitor event error", ex);
            }
        }

        public void Stop()
        {
            if (!_isRunning) return;
            _processWatcher?.Stop();
            _processWatcher?.Dispose();
            _processWatcher = null;
            _isRunning = false;
        }

        public void Dispose()
        {
            Stop();
            GC.SuppressFinalize(this);
        }
    }
}
