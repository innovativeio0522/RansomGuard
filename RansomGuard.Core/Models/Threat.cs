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
        public string Description { get; set; } = string.Empty;
        public string Path { get; set; } = string.Empty;
        public string ProcessName { get; set; } = string.Empty;
        public int ProcessId { get; set; }
        public DateTime Timestamp { get; set; } = DateTime.Now;
        public ThreatSeverity Severity { get; set; } = ThreatSeverity.Low;
        public string ActionTaken { get; set; } = "Monitored";
        
        /// <summary>
        /// Indicates whether this threat requires immediate user confirmation.
        /// Used for mass encryption events that need user approval before auto-mitigation.
        /// </summary>
        public bool RequiresUserConfirmation { get; set; } = false;
        
        /// <summary>
        /// List of files affected by this threat (used for mass encryption events).
        /// </summary>
        public List<string> AffectedFiles { get; set; } = new();
    }
}
