namespace RansomGuard.Core.Models
{
    public class ProcessInfo
    {
        public int Pid { get; set; }
        public string Name { get; set; } = string.Empty;
        public double CpuUsage { get; set; }
        public long MemoryUsage { get; set; }
        public double MemoryMb => MemoryUsage / (1024.0 * 1024.0);
        public double IoRate { get; set; }
        public bool IsTrusted { get; set; } = true;
        public string SignatureStatus { get; set; } = "Verified";

        // UI Helpers to bypass complex XAML Trigger issues
        public string WhitelistActionText => SignatureStatus == "User Whitelisted" ? "Un-whitelist" : "Whitelist";
        public string WhitelistActionColor => SignatureStatus == "User Whitelisted" ? "#ff5451" : "#00a572";
    }
}
