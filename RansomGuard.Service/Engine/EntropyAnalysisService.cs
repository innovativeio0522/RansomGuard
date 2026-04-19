using System;
using System.Collections.Generic;
using System.IO;

namespace RansomGuard.Service.Engine
{
    /// <summary>
    /// Provides stateless file-heuristic analysis: Shannon entropy calculation,
    /// suspicious extension checking, and rename-pattern detection.
    /// Extracted from SentinelEngine to reduce class size (#29).
    /// </summary>
    internal class EntropyAnalysisService : IEntropyAnalyzer
    {
        private static readonly HashSet<string> SuspiciousExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".locked", ".encrypted", ".crypty", ".wannacry", ".locky", ".crypt", ".enc"
        };

        private static readonly HashSet<string> MediaExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            // Images
            ".png", ".jpg", ".jpeg", ".gif", ".bmp", ".webp",
            // Video
            ".mp4", ".mov", ".avi", ".mkv",
            // Compressed (naturally high entropy — avoid false positives)
            ".zip", ".rar", ".7z", ".gz"
        };

        /// <summary>
        /// Returns true if the file has a known ransomware-associated extension.
        /// Uses a <see cref="HashSet{T}"/> for O(1) lookup instead of a linear array scan.
        /// </summary>
        public bool IsSuspiciousExtension(string path)
            => SuspiciousExtensions.Contains(Path.GetExtension(path));

        /// <summary>
        /// Returns true if the file is a media or archive type that is naturally high-entropy,
        /// so a higher entropy threshold is applied before flagging it.
        /// </summary>
        public bool IsMediaFile(string path)
            => MediaExtensions.Contains(Path.GetExtension(path));

        /// <summary>
        /// Returns true if the rename action target name ends with a known ransomware extension.
        /// </summary>
        public bool IsSuspiciousRenamePattern(string action)
        {
            if (!action.Contains("RENAMED")) return false;
            
            foreach (var ext in SuspiciousExtensions)
            {
                if (action.Contains(ext, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        /// <summary>
        /// Calculates the Shannon entropy of the first 4 KB of a file.
        /// Encrypted files typically exhibit entropy close to 8.0 (maximum).
        /// Returns 0 if the file cannot be read.
        /// </summary>
        public double CalculateShannonEntropy(string path)
        {
            try
            {
                if (!File.Exists(path)) return 0;

                byte[] buffer = new byte[4096];
                int bytesRead;
                using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                {
                    bytesRead = stream.Read(buffer, 0, buffer.Length);
                }

                if (bytesRead == 0) return 0;

                var counts = new int[256];
                for (int i = 0; i < bytesRead; i++) counts[buffer[i]]++;

                double entropy = 0;
                for (int i = 0; i < 256; i++)
                {
                    if (counts[i] == 0) continue;
                    double p = (double)counts[i] / bytesRead;
                    entropy -= p * Math.Log2(p);
                }
                return Math.Round(entropy, 2);
            }
            catch { return 0; }
        }
    }
}
