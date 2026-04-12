using CommunityToolkit.Mvvm.ComponentModel;
using System;

namespace RansomGuard.ViewModels
{
    public partial class ReportsViewModel : ViewModelBase
    {
        [ObservableProperty]
        private string _lastScanDate = DateTime.Now.AddDays(-1).ToString("MMM dd, yyyy HH:mm");

        [ObservableProperty]
        private int _totalScans = 142;

        [ObservableProperty]
        private int _securityScore = 94;

        public ReportsViewModel()
        {
        }
    }
}
