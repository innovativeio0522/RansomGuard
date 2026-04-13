using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RansomGuard.Core.Models;
using RansomGuard.Core.Interfaces;
using RansomGuard.Services;
using System.Collections.ObjectModel;

namespace RansomGuard.ViewModels
{
    public partial class ProcessMonitorViewModel : ViewModelBase
    {
        private readonly ISystemMonitorService _monitorService;

        public ObservableCollection<ProcessInfo> ActiveProcesses { get; } = new();

        public ProcessMonitorViewModel(ISystemMonitorService monitorService)
        {
            _monitorService = monitorService;
            LoadData();
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
    }
}
