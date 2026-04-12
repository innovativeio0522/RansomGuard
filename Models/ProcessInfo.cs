namespace RansomGuard.Models
{
    public class ProcessInfo
    {
        public int Pid { get; set; }
        public string Name { get; set; } = string.Empty;
        public double CpuUsage { get; set; }
        public long MemoryUsage { get; set; }
        public double IoRate { get; set; }
        public bool IsTrusted { get; set; } = true;
        public string SignatureStatus { get; set; } = "Verified";
    }
}
