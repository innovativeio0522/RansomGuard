namespace RansomGuard.Core.Models
{
    /// <summary>One time-slot in the CPU execution timeline chart.</summary>
    public class CpuDataPoint
    {
        public double KernelH { get; set; }
        public double UserH   { get; set; }
    }
}
