using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RansomGuard.Core.Models;
using RansomGuard.Core.Interfaces;
using RansomGuard.Core.IPC;
using RansomGuard.Core.Helpers;
using RansomGuard.Core.Configuration;
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
        private readonly Action _processListUpdatedHandler;
        private bool _disposed;

        private List<ProcessInfo> _allProcesses = new();
        private readonly object _processesLock = new();
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
                Interval = TimeSpan.FromSeconds(AppConstants.Timers.ProcessMonitorRefreshSeconds)
            };
            _refreshTimer.Tick += (s, e) => LoadData();
            _refreshTimer.Start();

            // Store handler reference for proper cleanup
            _processListUpdatedHandler = () => {
                System.Diagnostics.Debug.WriteLine("[ProcessMonitorViewModel] ProcessListUpdated event received");
                System.Windows.Application.Current.Dispatcher.Invoke(() => LoadData());
            };
            
            // Reactive update: Refresh instantly when a new process list arrives from the service
            _monitorService.ProcessListUpdated += _processListUpdatedHandler;
        }

        partial void OnSearchQueryChanged(string value)
        {
            ApplyFilter();
        }

        private void LoadData()
        {
            try
            {
                // Validate telemetry data
                var telemetry = _monitorService?.GetTelemetry();
                if (telemetry == null)
                {
                    System.Diagnostics.Debug.WriteLine("[ProcessMonitorViewModel] Telemetry is null - service may be disconnected");
                    FileLogger.Log("ui_process.log", "[LoadData] Telemetry is null");
                    telemetry = new TelemetryData(); // Use default values
                }
                
                // Failsafe: if IPC transport drops these specific values to 0 during a race condition, calculate them locally.
                int activeThreads = telemetry.ActiveThreadsCount;
                double trustedPercent = telemetry.TrustedProcessPercent;
                int suspiciousCount = telemetry.SuspiciousProcessCount;
                double cpuUsage = telemetry.CpuUsage;
                double kernelCpu = telemetry.KernelCpuUsage;
                double userCpu = telemetry.UserCpuUsage;
                
                if (activeThreads == 0)
                {
                    var procs = System.Diagnostics.Process.GetProcesses();
                    foreach (var p in procs) 
                    { 
                        try 
                        { 
                            activeThreads += p.Threads.Count; 
                        } 
                        catch (Exception ex) 
                        { 
                            // Process may have exited - this is expected
                            System.Diagnostics.Debug.WriteLine($"[ProcessMonitorViewModel] Failed to get thread count for {p.ProcessName}: {ex.Message}");
                        } 
                    }
                    int total = procs.Length;
                    trustedPercent = 100;
                    suspiciousCount = 0;
                }

                ActiveThreads = $"{activeThreads:N0}";
                CpuLoad = $"{cpuUsage:F1}%";
                CpuLoadValue = cpuUsage;
                TrustedProcPercent = $"{trustedPercent:F1}%";
                SuspiciousCount = suspiciousCount.ToString("D2");

                UpdateChart(kernelCpu, userCpu);

                // Validate process list
                var processes = _monitorService?.GetActiveProcesses();
                if (processes == null)
                {
                    System.Diagnostics.Debug.WriteLine("[ProcessMonitorViewModel] Process list is null - service may be disconnected");
                    FileLogger.Log("ui_process.log", "[LoadData] Process list is null");
                    return; // Don't update process list if service is unavailable
                }

                var validProcesses = processes.Where(p => p != null).ToList();
                var debugMsg = $"[LoadData] Retrieved {validProcesses.Count} processes. IsConnected={_monitorService?.IsConnected ?? false}";
                System.Diagnostics.Debug.WriteLine(debugMsg);
                FileLogger.Log("ui_process.log", debugMsg);
                
                lock (_processesLock)
                {
                    _allProcesses.Clear();
                    _allProcesses.AddRange(validProcesses);
                }

                System.Diagnostics.Debug.WriteLine($"[LoadData] _allProcesses has {_allProcesses.Count} items");
                System.Diagnostics.Debug.WriteLine($"[LoadData] ActiveProcesses has {ActiveProcesses.Count} items before ApplyFilter");

                // Auto-select most suspicious process for Alert Detail if available
                lock (_processesLock)
                {
                    if (SelectedProcess == null || !_allProcesses.Any(p => p != null && p.Pid == SelectedProcess.Pid))
                    {
                        SelectedProcess = _allProcesses.FirstOrDefault(p => p != null && !p.IsTrusted) ?? _allProcesses.FirstOrDefault(p => p != null);
                        System.Diagnostics.Debug.WriteLine($"[LoadData] SelectedProcess set to {SelectedProcess?.Name ?? "null"}");
                    }
                }

                ApplyFilter();
            }
            catch (Exception ex)
            {
                var errorMsg = $"[LoadData] EXCEPTION: {ex.Message}\n{ex.StackTrace}";
                System.Diagnostics.Debug.WriteLine(errorMsg);
                FileLogger.LogError("ui_process.log", "[LoadData] EXCEPTION", ex);
            }
        }

        private void ApplyFilter()
        {
            // Create a snapshot of _allProcesses to avoid collection modification during iteration
            List<ProcessInfo> snapshot;
            lock (_processesLock)
            {
                snapshot = _allProcesses.ToList();
            }
            
            var filtered = string.IsNullOrWhiteSpace(SearchQuery) 
                ? snapshot 
                : snapshot.Where(p => p != null && 
                                      ((p.Name != null && p.Name.Contains(SearchQuery, StringComparison.OrdinalIgnoreCase)) || 
                                       p.Pid.ToString().Contains(SearchQuery))).ToList();

            System.Diagnostics.Debug.WriteLine($"[DEBUG] ApplyFilter: filtered has {filtered.Count} items");
            FileLogger.Log("ui_process.log", $"[ApplyFilter] filtered={filtered.Count}");

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
            FileLogger.Log("ui_process.log", $"[ApplyFilter] ActiveProcesses.Count={ActiveProcesses.Count}");
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
                // Create a snapshot to avoid collection modification during iteration
                List<ProcessInfo> processSnapshot;
                lock (_processesLock)
                {
                    processSnapshot = _allProcesses.ToList();
                }
                
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
                            foreach (var p in processSnapshot)
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
                    try 
                    { 
                        System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{path}\""); 
                    } 
                    catch (Exception ex) 
                    { 
                        System.Diagnostics.Debug.WriteLine($"[ProcessMonitorViewModel] Failed to open explorer: {ex.Message}");
                    }
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
            
            _refreshTimer.Stop();
            
            // Unsubscribe from event to prevent memory leak
            if (_monitorService != null)
            {
                _monitorService.ProcessListUpdated -= _processListUpdatedHandler;
            }
        }
    }
}
