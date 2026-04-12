using CommunityToolkit.Mvvm.ComponentModel;

namespace RansomGuard.ViewModels
{
    public partial class SettingsViewModel : ViewModelBase
    {
        [ObservableProperty]
        private bool _isRealTimeProtectionEnabled = true;

        [ObservableProperty]
        private bool _isCloudAnalysisEnabled = true;

        [ObservableProperty]
        private bool _isAutoQuarantineEnabled = true;

        [ObservableProperty]
        private string _activeEngineVersion = "Sentinel v4.2.0";

        public SettingsViewModel()
        {
        }
    }
}
