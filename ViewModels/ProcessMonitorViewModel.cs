using CommunityToolkit.Mvvm.ComponentModel;
using RansomGuard.Models;
using RansomGuard.Services;
using System.Collections.ObjectModel;

namespace RansomGuard.ViewModels
{
    public partial class ProcessMonitorViewModel : ViewModelBase
    {
        private readonly ISystemMonitorService _monitorService;

        public ObservableCollection<ProcessInfo> ActiveProcesses { get; } = new();

        public ProcessMonitorViewModel()
        {
            _monitorService = new MockMonitorService();
            LoadData();
        }

        private void LoadData()
        {
            foreach (var process in _monitorService.GetActiveProcesses())
            {
                ActiveProcesses.Add(process);
            }
        }
    }
}
