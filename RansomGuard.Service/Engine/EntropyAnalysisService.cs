using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace RansomGuard.Service.Engine
{
    /// <summary>
    /// Provides high-performance file-heuristic analysis: Shannon entropy calculation with 
    /// multi-point sampling and log-table optimization.
    /// </summary>
    internal class EntropyAnalysisService : IEntropyAnalyzer
    {
        private static readonly HashSet<string> SuspiciousExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".locked", ".encrypted", ".crypty", ".wannacry", ".locky", ".crypt", ".enc",
            ".ransom", ".ryk", ".hive", ".blackcat", ".wncry", ".zepto", ".cerber",
            ".dharma", ".phobos", ".stop", ".djvu", ".sodinokibi", ".revil"
        };

        private static readonly HashSet<string> MediaExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".png", ".jpg", ".jpeg", ".gif", ".bmp", ".webp",
            ".mp4", ".mov", ".avi", ".mkv", ".mp3", ".ogg", ".wav", ".aac", ".flac", ".m4a", ".swf",
            ".zip", ".rar", ".7z", ".gz", ".tar", ".iso", ".img", ".apk", ".cab",
            ".pdf", ".docx", ".xlsx", ".pptx", ".doc", ".xls", ".ppt"
        };
        
        private static readonly HashSet<string> HighEntropyExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".exe", ".dll", ".sys", ".pdb", ".obj", ".lib", ".exp", ".pyc", ".class", 
            ".o", ".suo", ".pdb7", ".user", ".tlb", ".ax", ".node",
            ".msi", ".dat", ".blog", ".pfl", ".msg", ".bin"
        };

        // Optimized log table for 4KB chunks: f(count) = (count/4096) * log2(count/4096)
        private static readonly double[] EntropyTable = new double[4097];

        static EntropyAnalysisService()
        {
            const double log2 = 0.693147180559945; // ln(2)
            for (int i = 1; i <= 4096; i++)
            {
                double p = (double)i / 4096;
                EntropyTable[i] = -(p * (Math.Log(p) / log2));
            }
        }

        public bool IsSuspiciousExtension(string path)
            => SuspiciousExtensions.Contains(Path.GetExtension(path));

        public bool IsMediaFile(string path)
            => MediaExtensions.Contains(Path.GetExtension(path));

        public bool IsHighEntropyExtension(string path)
            => HighEntropyExtensions.Contains(Path.GetExtension(path));

        public bool ShouldSkipDirectory(string path)
        {
            try
            {
                var directoryName = Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
                return RansomGuard.Core.Services.ConfigurationService.Instance.ExcludedFolderNames
                    .Any(ex => string.Equals(ex, directoryName, StringComparison.OrdinalIgnoreCase));
            }
            catch { return false; }
        }

        public bool IsSuspiciousRenamePattern(string action)
        {
            if (!action.Contains("RENAMED")) return false;
            return SuspiciousExtensions.Any(ext => action.Contains(ext, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// Calculates the Shannon entropy of a file using multi-point sampling.
        /// Samples the head, middle, and tail (4KB each) for comprehensive analysis.
        /// Optimized with a pre-computed probability table to minimize floating point operations.
        /// </summary>
        public double CalculateShannonEntropy(string path)
        {
            try
            {
                if (!File.Exists(path)) return 0;
                var info = new FileInfo(path);
                if (info.Length == 0) return 0;

                using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                
                // For small files (< 12KB), just read the whole thing (up to 4KB)
                if (info.Length < 12288)
                {
                    return CalculateChunkEntropy(stream, 0, 4096);
                }

                // For large files, take the average entropy of three key points:
                // Start, Middle (50%), and End (last 4KB)
                double e1 = CalculateChunkEntropy(stream, 0, 4096);
                double e2 = CalculateChunkEntropy(stream, info.Length / 2, 4096);
                double e3 = CalculateChunkEntropy(stream, info.Length - 4096, 4096);

                // Return the peak entropy (if any part is encrypted, it's a threat)
                return Math.Round(Math.Max(e1, Math.Max(e2, e3)), 2);
            }
            catch { return 0; }
        }

        private double CalculateChunkEntropy(FileStream stream, long offset, int length)
        {
            byte[] buffer = new byte[length];
            stream.Seek(offset, SeekOrigin.Begin);
            int bytesRead = stream.Read(buffer, 0, length);
            if (bytesRead == 0) return 0;

            var counts = new int[256];
            for (int i = 0; i < bytesRead; i++) counts[buffer[i]]++;

            double entropy = 0;
            // If we read exactly 4KB, use the optimized lookup table
            if (bytesRead == 4096)
            {
                for (int i = 0; i < 256; i++)
                {
                    if (counts[i] > 0) entropy += EntropyTable[counts[i]];
                }
            }
            else
            {
                // Fallback for smaller/final chunks
                for (int i = 0; i < 256; i++)
                {
                    if (counts[i] == 0) continue;
                    double p = (double)counts[i] / bytesRead;
                    entropy -= p * Math.Log2(p);
                }
            }
            return entropy;
        }
    }
}
