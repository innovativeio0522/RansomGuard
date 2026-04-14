using CommunityToolkit.Mvvm.ComponentModel;
using RansomGuard.Core.Models;

namespace RansomGuard.ViewModels
{
    /// <summary>
    /// UI wrapper for a quarantined threat that adds selection state.
    /// </summary>
    public partial class QuarantineItemViewModel : ViewModelBase
    {
        [ObservableProperty]
        private bool _isSelected;

        public Threat Threat { get; }

        public QuarantineItemViewModel(Threat threat)
        {
            Threat = threat;
        }

        // Helper properties for direct binding in XAML
        public string Name => Threat.Name;
        public string Path => Threat.Path;
        public string Description => Threat.Description;
        public System.DateTime Timestamp => Threat.Timestamp;
        public ThreatSeverity Severity => Threat.Severity;
    }
}
