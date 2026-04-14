using CommunityToolkit.Mvvm.ComponentModel;
using RansomGuard.Core.Models;
using RansomGuard.Core.Interfaces;
using RansomGuard.Services;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Windows.Threading;

namespace RansomGuard.ViewModels
{
    public partial class FileActivityViewModel : ViewModelBase, IDisposable
    {
        private const int MaxRecentActivities = 150;
        
        private readonly ISystemMonitorService _monitorService;
        private readonly DispatcherTimer _bufferTimer;
        private readonly ConcurrentQueue<FileActivity> _activityBuffer = new();
        private bool _disposed;

        public ObservableCollection<FileActivity> RecentActivities { get; } = new();

        public FileActivityViewModel(ISystemMonitorService monitorService)
        {
            _monitorService = monitorService;
            
            // Initial load
            foreach (var activity in _monitorService.GetRecentFileActivities())
            {
                RecentActivities.Add(activity);
            }

            // Subscribe to live updates (enqueue only — no direct UI update)
            _monitorService.FileActivityDetected += OnFileActivityDetected;

            // Flush buffer to UI every 500ms
            _bufferTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
            _bufferTimer.Tick += (s, e) => ProcessBuffer();
            _bufferTimer.Start();
        }

        private void OnFileActivityDetected(FileActivity activity)
        {
            // Thread-safe enqueue — no Dispatcher.Invoke here
            _activityBuffer.Enqueue(activity);
        }

        private void ProcessBuffer()
        {
            if (_activityBuffer.IsEmpty) return;

            var batch = new List<FileActivity>();
            while (_activityBuffer.TryDequeue(out var item))
                batch.Add(item);

            foreach (var item in batch)
            {
                RecentActivities.Insert(0, item);
                if (RecentActivities.Count > MaxRecentActivities)
                    RecentActivities.RemoveAt(RecentActivities.Count - 1);
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            _bufferTimer?.Stop();

            if (_monitorService != null)
            {
                _monitorService.FileActivityDetected -= OnFileActivityDetected;
            }
        }
    }
}
