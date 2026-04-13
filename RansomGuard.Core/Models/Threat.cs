namespace RansomGuard.Core.Models
{
    public enum ThreatSeverity
    {
        Low,
        Medium,
        High,
        Critical
    }

    public class Threat
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string Name { get; set; } = string.Empty;
        public string Path { get; set; } = string.Empty;
        public string ProcessName { get; set; } = string.Empty;
        public DateTime Timestamp { get; set; } = DateTime.Now;
        public ThreatSeverity Severity { get; set; } = ThreatSeverity.Low;
        public string ActionTaken { get; set; } = "Monitored";
    }
}
