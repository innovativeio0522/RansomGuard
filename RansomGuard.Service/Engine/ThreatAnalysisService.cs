using System;
using System.IO;
using RansomGuard.Core.Models;
using RansomGuard.Core.Services;

namespace RansomGuard.Service.Engine
{
    /// <summary>
    /// Analyzes file events for threats using entropy calculation and pattern detection.
    /// </summary>
    public class ThreatAnalysisService
    {
        private readonly IEntropyAnalyzer _entropyAnalyzer;
        private double _lastEntropyScore = 1.5;

        public double LastEntropyScore => _lastEntropyScore;

        public ThreatAnalysisService(IEntropyAnalyzer entropyAnalyzer)
        {
            _entropyAnalyzer = entropyAnalyzer ?? throw new ArgumentNullException(nameof(entropyAnalyzer));
        }

        /// <summary>
        /// Analyzes a file event and returns threat analysis results.
        /// </summary>
        public ThreatAnalysisResult AnalyzeFile(string path, string action, bool isTrustedProcess, int sensitivityLevel)
        {
            var result = new ThreatAnalysisResult
            {
                Path = path,
                Action = action
            };

            // Skip excluded directories
            bool skipDir = _entropyAnalyzer.ShouldSkipDirectory(path) || 
                          path.Split(Path.DirectorySeparatorChar).Any(part => _entropyAnalyzer.ShouldSkipDirectory(part));
            
            if (skipDir)
            {
                result.ShouldSkip = true;
                result.SkipReason = "Excluded directory";
                return result;
            }

            // Skip if it's a directory
            if (Directory.Exists(path) && !File.Exists(path))
            {
                result.ShouldSkip = true;
                result.SkipReason = "Is directory";
                return result;
            }

            // Check for suspicious extension
            result.HasSuspiciousExtension = _entropyAnalyzer.IsSuspiciousExtension(path);
            result.IsMediaFile = _entropyAnalyzer.IsMediaFile(path);
            result.IsBinaryFile = _entropyAnalyzer.IsHighEntropyExtension(path);

            // Calculate entropy for relevant actions
            if (action == "CHANGED" || action == "CREATED" || action.Contains("RENAMED") || 
                result.HasSuspiciousExtension || result.IsMediaFile || result.IsBinaryFile)
            {
                var sw = System.Diagnostics.Stopwatch.StartNew();
                result.Entropy = _entropyAnalyzer.CalculateShannonEntropy(path);
                sw.Stop();
                _lastEntropyScore = result.Entropy;
                PerformanceMonitor.Instance.RecordEntropyCalculation(sw.Elapsed.TotalMilliseconds, result.Entropy);
            }

            // Determine entropy threshold based on file type and trust level
            result.EntropyThreshold = GetEntropyThreshold(path, isTrustedProcess, result.IsMediaFile, result.IsBinaryFile, sensitivityLevel);
            result.IsHighEntropy = result.Entropy > result.EntropyThreshold;

            // Check for suspicious patterns
            result.HasSuspiciousRenamePattern = _entropyAnalyzer.IsSuspiciousRenamePattern(action);
            result.HasExtensionMismatch = _entropyAnalyzer.IsExtensionMismatch(path);

            // Determine if suspicious
            result.IsSuspicious = result.IsHighEntropy || 
                                 result.HasSuspiciousRenamePattern || 
                                 result.HasSuspiciousExtension || 
                                 result.HasExtensionMismatch;

            // Determine threat reason
            if (result.IsSuspicious)
            {
                if (result.HasSuspiciousExtension)
                    result.ThreatReason = "Suspicious Extension";
                else if (result.HasExtensionMismatch)
                    result.ThreatReason = "File Type Mismatch";
                else if (result.IsHighEntropy)
                    result.ThreatReason = "High Entropy Data";
                else
                    result.ThreatReason = "Suspicious Pattern";
            }

            return result;
        }

        private double GetEntropyThreshold(string path, bool isTrustedProcess, bool isMedia, bool isBinary, int sensitivityLevel)
        {
            double baseThreshold = sensitivityLevel switch
            {
                1 => 7.8, // Low
                2 => 7.5, // Medium
                3 => 7.2, // High
                4 => 6.8, // Paranoid
                _ => 7.2
            };

            if (isMedia || isBinary)
            {
                if (isTrustedProcess) return 7.995;
                return 7.99;
            }

            return baseThreshold;
        }
    }

    /// <summary>
    /// Results of threat analysis for a file event.
    /// </summary>
    public class ThreatAnalysisResult
    {
        public string Path { get; set; } = string.Empty;
        public string Action { get; set; } = string.Empty;
        public bool ShouldSkip { get; set; }
        public string SkipReason { get; set; } = string.Empty;
        public bool HasSuspiciousExtension { get; set; }
        public bool IsMediaFile { get; set; }
        public bool IsBinaryFile { get; set; }
        public double Entropy { get; set; }
        public double EntropyThreshold { get; set; }
        public bool IsHighEntropy { get; set; }
        public bool HasSuspiciousRenamePattern { get; set; }
        public bool HasExtensionMismatch { get; set; }
        public bool IsSuspicious { get; set; }
        public string ThreatReason { get; set; } = string.Empty;
    }
}
