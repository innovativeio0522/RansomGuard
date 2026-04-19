using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using RansomGuard.Core.Helpers;
using RansomGuard.Service.Services;

namespace RansomGuard.Service.Engine
{
    /// <summary>
    /// Handles all quarantine I/O: isolating suspicious files, restoring them to
    /// their original locations, permanent deletion, and storage reporting.
    /// Extracted from SentinelEngine to reduce class size (#29).
    /// </summary>
    internal class QuarantineService : IQuarantineService
    {
        private readonly IHistoryStore _historyStore;
        private readonly string _quarantinePath;

        public QuarantineService(IHistoryStore historyStore, string? quarantinePath = null)
        {
            _historyStore = historyStore;
            _quarantinePath = quarantinePath ?? PathConfiguration.QuarantinePath;
        }

        /// <summary>
        /// Moves a file to the quarantine directory and records metadata for restoration.
        /// </summary>
        public async Task QuarantineFile(string filePath)
        {
            await Task.Run(async () =>
            {
                try
                {
                    if (!File.Exists(filePath)) return;

                    string quarantineDir = _quarantinePath;
                    Directory.CreateDirectory(quarantineDir);

                    string dest    = Path.Combine(quarantineDir, Path.GetFileName(filePath) + ".quarantine");
                    string metaDest = dest + ".metadata";

                    string metadata = $"OriginalPath={filePath}\nQuarantinedAt={DateTime.Now:O}\nFileSize={new FileInfo(filePath).Length}";
                    File.WriteAllText(metaDest, metadata);
                    File.Move(filePath, dest, overwrite: true);

                    await _historyStore.UpdateThreatStatusAsync(filePath, "Quarantined").ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[QuarantineService] QuarantineFile error: {ex.Message}");
                }
            });
        }

        /// <summary>
        /// Restores a quarantined file to its original path using the stored metadata.
        /// </summary>
        public async Task RestoreQuarantinedFile(string quarantinePath)
        {
            await Task.Run(() =>
            {
                try
                {
                    if (!File.Exists(quarantinePath)) return;

                    string metaPath    = quarantinePath + ".metadata";
                    string originalPath = string.Empty;

                    if (File.Exists(metaPath))
                    {
                        foreach (var line in File.ReadAllLines(metaPath))
                        {
                            if (line.StartsWith("OriginalPath="))
                                originalPath = line.Substring("OriginalPath=".Length);
                        }
                    }

                    if (string.IsNullOrEmpty(originalPath) || originalPath == "Unknown Path")
                        throw new InvalidOperationException("Original path not found in metadata.");

                    string? destDir = Path.GetDirectoryName(originalPath);
                    if (!string.IsNullOrEmpty(destDir)) Directory.CreateDirectory(destDir);

                    File.Move(quarantinePath, originalPath, overwrite: false);
                    if (File.Exists(metaPath)) File.Delete(metaPath);

                    System.Diagnostics.Debug.WriteLine($"[QuarantineService] Restored: {originalPath}");
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[QuarantineService] RestoreQuarantinedFile error: {ex.Message}");
                    throw;
                }
            });
        }

        /// <summary>Permanently deletes a quarantined file and its metadata.</summary>
        public async Task DeleteQuarantinedFile(string quarantinePath)
        {
            await Task.Run(() =>
            {
                try
                {
                    string metaPath = quarantinePath + ".metadata";
                    if (File.Exists(quarantinePath)) File.Delete(quarantinePath);
                    if (File.Exists(metaPath))       File.Delete(metaPath);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[QuarantineService] DeleteQuarantinedFile error: {ex.Message}");
                    throw;
                }
            });
        }

        /// <summary>Purges quarantined files that are older than 30 days.</summary>
        public async Task ClearOldFiles()
        {
            await Task.Run(async () =>
            {
                try
                {
                    foreach (var file in GetQuarantinedFiles())
                    {
                        if (DateTime.Now - new FileInfo(file).LastWriteTime > TimeSpan.FromDays(30))
                            await DeleteQuarantinedFile(file).ConfigureAwait(false);
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[QuarantineService] ClearOldFiles error: {ex.Message}");
                }
            });
        }

        /// <summary>Returns the paths of all files currently in quarantine.</summary>
        public IEnumerable<string> GetQuarantinedFiles()
        {
            string dir = _quarantinePath;
            if (!Directory.Exists(dir)) return Enumerable.Empty<string>();
            try   { return Directory.EnumerateFiles(dir, "*.quarantine"); }
            catch { return Enumerable.Empty<string>(); }
        }

        /// <summary>Returns the total quarantine directory size in megabytes.</summary>
        public double GetStorageUsageMb()
        {
            string dir = _quarantinePath;
            if (!Directory.Exists(dir)) return 0;
            try
            {
                long total = new DirectoryInfo(dir).GetFiles("*.quarantine").Sum(f => f.Length);
                return total / (1024.0 * 1024.0);
            }
            catch { return 0; }
        }
    }
}
