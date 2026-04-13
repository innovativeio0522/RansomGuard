using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RansomGuard.Core.Models;
using RansomGuard.Core.Interfaces;
using RansomGuard.Services;
using System.Collections.ObjectModel;
using System.Windows.Threading;

namespace RansomGuard.ViewModels
{
    public partial class ProcessMonitorViewModel : ViewModelBase, IDisposable
    {
        private readonly ISystemMonitorService _monitorService;
        private readonly DispatcherTimer _refreshTimer;
        private bool _disposed;

        public ObservableCollection<ProcessInfo> ActiveProcesses { get; } = new();

        public ProcessMonitorViewModel(ISystemMonitorService monitorService)
        {
            _monitorService = monitorService;
            LoadData();

            // Auto-refresh process list every 3 seconds
            _refreshTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(3)
            };
            _refreshTimer.Tick += (s, e) => LoadData();
            _refreshTimer.Start();
        }

        private void LoadData()
        {
            ActiveProcesses.Clear();
            foreach (var process in _monitorService.GetActiveProcesses())
            {
                ActiveProcesses.Add(process);
            }
        }

        [RelayCommand]
        private async Task KillProcess(ProcessInfo? process)
        {
            if (process != null)
            {
                await _monitorService.KillProcess(process.Pid);
                LoadData();
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            
            // Stop and dispose refresh timer
            if (_refreshTimer != null)
            {
                _refreshTimer.Stop();
            }
        }
    }
}
