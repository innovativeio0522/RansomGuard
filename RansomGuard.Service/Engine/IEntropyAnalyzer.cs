using System;

namespace RansomGuard.Service.Engine
{
    public interface IEntropyAnalyzer
    {
        bool IsSuspiciousExtension(string path);
        bool IsMediaFile(string path);
        bool IsSuspiciousRenamePattern(string action);
        double CalculateShannonEntropy(string path);
    }
}
