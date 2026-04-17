namespace RansomGuard.Core.Models
{
    public class FileActivity
    {
        public DateTime Timestamp { get; set; } = DateTime.Now;
        public string Action { get; set; } = "READ";
        public string ActivityType => Action; // Alias for UI

        public string FilePath { get; set; } = string.Empty;
        
        public double Entropy { get; set; } = 0.0;
        public double EntropyScore => Entropy; // Alias for UI

        public bool IsSuspicious { get; set; } = false;
        
        public string ProcessName { get; set; } = "System";
        public string ResponsibleProcess => ProcessName; // Alias for UI
    }
}
