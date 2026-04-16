using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace RansomGuard.Core.Models
{
    public class ProcessInfo : INotifyPropertyChanged
    {
        private int _pid;
        private string _name = string.Empty;
        private double _cpuUsage;
        private long _memoryUsage;
        private double _ioRate;
        private bool _isTrusted = true;
        private string _signatureStatus = "Verified";

        public int Pid
        {
            get => _pid;
            set => SetProperty(ref _pid, value);
        }

        public string Name
        {
            get => _name;
            set => SetProperty(ref _name, value);
        }

        public double CpuUsage
        {
            get => _cpuUsage;
            set => SetProperty(ref _cpuUsage, value);
        }

        public long MemoryUsage
        {
            get => _memoryUsage;
            set => SetProperty(ref _memoryUsage, value);
        }

        public double MemoryMb => MemoryUsage / (1024.0 * 1024.0);

        public double IoRate
        {
            get => _ioRate;
            set => SetProperty(ref _ioRate, value);
        }

        public bool IsTrusted
        {
            get => _isTrusted;
            set => SetProperty(ref _isTrusted, value);
        }

        public string SignatureStatus
        {
            get => _signatureStatus;
            set => SetProperty(ref _signatureStatus, value);
        }

        // UI Helpers to bypass complex XAML Trigger issues
        [System.Text.Json.Serialization.JsonIgnore]
        public string WhitelistActionText => SignatureStatus == "User Whitelisted" ? "Un-whitelist" : "Whitelist";
        
        [System.Text.Json.Serialization.JsonIgnore]
        public string WhitelistActionColor => SignatureStatus == "User Whitelisted" ? "#ff5451" : "#00a572";

        /// <summary>
        /// Updates the current process info from another instance without breaking object references.
        /// This is critical for maintaining UI selection and stability in WPF.
        /// </summary>
        public void UpdateFrom(ProcessInfo other)
        {
            if (other == null) return;
            
            // Only update volatile metrics, preserving identity and status
            CpuUsage = other.CpuUsage;
            MemoryUsage = other.MemoryUsage;
            IoRate = other.IoRate;
            IsTrusted = other.IsTrusted;
            SignatureStatus = other.SignatureStatus;
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
        {
            if (Equals(field, value)) return false;
            field = value;
            OnPropertyChanged(propertyName);
            return true;
        }
    }
}
