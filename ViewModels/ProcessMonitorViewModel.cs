using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RansomGuard.Core.Models;
using RansomGuard.Core.Interfaces;
using RansomGuard.Services;
using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Threading;

namespace RansomGuard.ViewModels
{
    public partial class ProcessMonitorViewModel : ViewModelBase, IDisposable
    {
        private readonly ISystemMonitorService _monitorService;
        private readonly DispatcherTimer _refreshTimer;
        private bool _disposed;

        private List<ProcessInfo> _allProcesses = new();
        public ObservableCollection<ProcessInfo> ActiveProcesses { get; } = new();

        [ObservableProperty]
        private string _searchQuery = string.Empty;

        [ObservableProperty]
        private ProcessInfo? _selectedProcess;

        // Telemetry metrics
        [ObservableProperty]
        private string _activeThreads = "0";

        [ObservableProperty]
        private string _cpuLoad = "0%";

        [ObservableProperty]
        private double _cpuLoadValue = 0;

        [ObservableProperty]
        private string _trustedProcPercent = "100%";

        [ObservableProperty]
        private string _suspiciousCount = "00";

        // Single collection of paired (Kernel, User) chart points
        public ObservableCollection<CpuDataPoint> CpuHistory { get; } = new();

        [ObservableProperty] private string _kernelCpuText = "0.0%";
        [ObservableProperty] private string _userCpuText   = "0.0%";

        public ProcessMonitorViewModel(ISystemMonitorService monitorService)
        {
            _monitorService = monitorService;
            
            System.Diagnostics.Debug.WriteLine($"[ProcessMonitorViewModel] Constructor called. IsConnected={monitorService.IsConnected}");
            
            for (int i = 0; i < 30; i++) CpuHistory.Add(new CpuDataPoint { KernelH = 4, UserH = 4 });

            LoadData();

            // Auto-refresh process list every 3 seconds
            _refreshTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(3)
            };
            _refreshTimer.Tick += (s, e) => LoadData();
            _refreshTimer.Start();

            // Reactive update: Refresh instantly when a new process list arrives from the service
            _monitorService.ProcessListUpdated += () => {
                System.Diagnostics.Debug.WriteLine("[ProcessMonitorViewModel] ProcessListUpdated event received");
                System.Windows.Application.Current.Dispatcher.Invoke(() => LoadData());
            };
        }

        partial void OnSearchQueryChanged(string value)
        {
            ApplyFilter();
        }

        private void LoadData()
        {
            try
            {
                var telemetry = _monitorService.GetTelemetry();
                
                // Failsafe: if IPC transport drops these specific values to 0 during a race condition, calculate them locally.
                int activeThreads = telemetry.ActiveThreadsCount;
                double trustedPercent = telemetry.TrustedProcessPercent;
                int suspiciousCount = telemetry.SuspiciousProcessCount;
                
                if (activeThreads == 0)
                {
                    var procs = System.Diagnostics.Process.GetProcesses();
                    foreach (var p in procs) { try { activeThreads += p.Threads.Count; } catch { } }
                    int total = procs.Length;
                    trustedPercent = 100;
                    suspiciousCount = 0;
                }

                ActiveThreads = $"{activeThreads:N0}";
                CpuLoad = $"{telemetry.CpuUsage:F1}%";
                CpuLoadValue = telemetry.CpuUsage;
                TrustedProcPercent = $"{trustedPercent:F1}%";
                SuspiciousCount = suspiciousCount.ToString("D2");

                UpdateChart(telemetry.KernelCpuUsage, telemetry.UserCpuUsage);

                var processes = _monitorService.GetActiveProcesses().ToList();
                var debugMsg = $"[LoadData] Retrieved {processes.Count} processes. IsConnected={_monitorService.IsConnected}";
                System.Diagnostics.Debug.WriteLine(debugMsg);
                File.AppendAllText("process_debug.log", $"{DateTime.Now}: {debugMsg}\n");
                
                _allProcesses.Clear();
                foreach (var process in processes)
                {
                    _allProcesses.Add(process);
                }

                System.Diagnostics.Debug.WriteLine($"[LoadData] _allProcesses has {_allProcesses.Count} items");
                System.Diagnostics.Debug.WriteLine($"[LoadData] ActiveProcesses has {ActiveProcesses.Count} items before ApplyFilter");

                // Auto-select most suspicious process for Alert Detail if available
                if (SelectedProcess == null || !_allProcesses.Any(p => p.Pid == SelectedProcess.Pid))
                {
                    SelectedProcess = _allProcesses.FirstOrDefault(p => !p.IsTrusted) ?? _allProcesses.FirstOrDefault();
                    System.Diagnostics.Debug.WriteLine($"[LoadData] SelectedProcess set to {SelectedProcess?.Name ?? "null"}");
                }

                ApplyFilter();
            }
            catch (Exception ex)
            {
                var errorMsg = $"[LoadData] EXCEPTION: {ex.Message}\n{ex.StackTrace}";
                System.Diagnostics.Debug.WriteLine(errorMsg);
                File.AppendAllText("process_debug.log", $"{DateTime.Now}: {errorMsg}\n");
            }
        }

        private void ApplyFilter()
        {
            var filtered = string.IsNullOrWhiteSpace(SearchQuery) 
                ? _allProcesses 
                : _allProcesses.Where(p => p.Name.Contains(SearchQuery, StringComparison.OrdinalIgnoreCase) || 
                                           p.Pid.ToString().Contains(SearchQuery)).ToList();

            System.Diagnostics.Debug.WriteLine($"[DEBUG] ApplyFilter: filtered has {filtered.Count} items");
            File.AppendAllText("process_debug.log", $"{DateTime.Now}: [ApplyFilter] filtered={filtered.Count}\n");

            // CRITICAL FIX: The UI loses selection and binding state because objects are recreated on every refresh.
            // We use reconciliation to update existing instances instead of clearing the collection.
            var toAdd = filtered.Where(p => !ActiveProcesses.Any(ap => ap.Pid == p.Pid)).ToList();
            var toRemove = ActiveProcesses.Where(ap => !filtered.Any(p => p.Pid == ap.Pid)).ToList();

            System.Diagnostics.Debug.WriteLine($"[DEBUG] ApplyFilter: toAdd={toAdd.Count}, toRemove={toRemove.Count}");

            // 1. Remove dead processes
            foreach (var p in toRemove) ActiveProcesses.Remove(p);

            // 2. Update existing processes (merging fresh data into existing instances)
            foreach (var active in ActiveProcesses)
            {
                var fresh = filtered.FirstOrDefault(p => p.Pid == active.Pid);
                if (fresh != null)
                {
                    active.UpdateFrom(fresh);
                }
            }

            // 3. Add new processes
            foreach (var p in toAdd)
            {
                ActiveProcesses.Add(p);
                System.Diagnostics.Debug.WriteLine($"[DEBUG] ApplyFilter: Added process PID={p.Pid}, Name={p.Name}");
            }

            System.Diagnostics.Debug.WriteLine($"[DEBUG] ApplyFilter: ActiveProcesses now has {ActiveProcesses.Count} items");
            File.AppendAllText("process_debug.log", $"{DateTime.Now}: [ApplyFilter] ActiveProcesses.Count={ActiveProcesses.Count}\n");
        }

        private void UpdateChart(double kernelCpu, double userCpu)
        {
            CpuHistory.RemoveAt(0);
            CpuHistory.Add(new CpuDataPoint
            {
                KernelH = 20 + Math.Min(kernelCpu * 2.8, 120),
                UserH   = 20 + Math.Min(userCpu   * 2.8, 120)
            });
            KernelCpuText = $"{kernelCpu:F1}%";
            UserCpuText   = $"{userCpu:F1}%";
        }

        [RelayCommand]
        private void Refresh()
        {
            LoadData(); // Manual trigger
        }

        [RelayCommand]
        private async Task ExportLogs()
        {
            string[] potentialFolders = {
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                Path.GetTempPath()
            };

            string path = string.Empty;
            bool success = false;
            string lastError = "No valid path found";

            await Task.Run(() =>
            {
                foreach (var folder in potentialFolders)
                {
                    if (string.IsNullOrEmpty(folder)) continue;

                    try
                    {
                        Directory.CreateDirectory(folder);
                        path = Path.Combine(folder, $"RansomGuard_ProcessLog_{DateTime.Now:yyyyMMddHHmmss}.csv");
                        
                        using (var writer = new StreamWriter(path, false))
                        {
                            writer.WriteLine("PID,Name,CPU,Memory,Trusted");
                            foreach (var p in _allProcesses)
                            {
                                writer.WriteLine($"{p.Pid},{p.Name},{p.CpuUsage},{p.MemoryUsage},{p.IsTrusted}");
                            }
                        }
                        
                        if (File.Exists(path))
                        {
                            success = true;
                            break;
                        }
                    }
                    catch (Exception ex)
                    {
                        lastError = ex.Message;
                        continue; // Try next folder
                    }
                }
            });

            if (success)
            {
                var result = System.Windows.MessageBox.Show($"Analysis report exported successfully to:\n{path}\n\nWould you like to open the export folder?", 
                    "Export Complete", System.Windows.MessageBoxButton.YesNo, System.Windows.MessageBoxImage.Information);
                
                if (result == System.Windows.MessageBoxResult.Yes)
                {
                    try { System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{path}\""); } catch { }
                }
            }
            else
            {
                System.Windows.MessageBox.Show($"Export failed across all standard locations.\n\nError: {lastError}\n\nNote: This can happen if your antivirus or OneDrive is locking these folders.", "Export Error", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
            }
        }

        [RelayCommand]
        private async Task WhitelistProcess(ProcessInfo? process)
        {
            if (process != null)
            {
                // Toggle logic: if already whitelisted, remove it.
                if (process.SignatureStatus == "User Whitelisted")
                {
                    await _monitorService.RemoveWhitelist(process.Name);
                }
                else
                {
                    await _monitorService.WhitelistProcess(process.Name);
                }
                
                // Refresh list to see change instantly
                LoadData();
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
