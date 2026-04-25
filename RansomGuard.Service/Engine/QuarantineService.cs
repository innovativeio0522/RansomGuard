using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security;
using System.Threading.Tasks;
using RansomGuard.Core.Helpers;
using RansomGuard.Service.Services;

namespace RansomGuard.Service.Engine
{
    /// <summary>
    /// Handles all quarantine I/O operations: isolating suspicious files, restoring them to
    /// their original locations, permanent deletion, and storage reporting.
    /// Extracted from SentinelEngine to reduce class size (#29).
    /// </summary>
    /// <remarks>
    /// This service provides secure file quarantine functionality with:
    /// - Metadata tracking for restoration
    /// - Path validation to prevent security vulnerabilities
    /// - Symlink/junction resolution on Windows
    /// - Automatic cleanup of old quarantined files
    /// </remarks>
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
        /// <param name="filePath">The full path to the file to quarantine</param>
        /// <returns>A task representing the asynchronous operation</returns>
        /// <remarks>
        /// Creates a .quarantine file and a .metadata file containing:
        /// - Original file path
        /// - Quarantine timestamp
        /// - Original file size
        /// Updates the threat status in the history database.
        /// </remarks>
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
        /// <param name="quarantinePath">The path to the quarantined file (with .quarantine extension)</param>
        /// <returns>A task representing the asynchronous operation</returns>
        /// <exception cref="InvalidOperationException">Thrown when metadata is missing or invalid</exception>
        /// <exception cref="SecurityException">Thrown when the restore path fails security validation</exception>
        /// <remarks>
        /// Validates the restore path to prevent:
        /// - Path traversal attacks
        /// - Restoration to system directories
        /// - Symlink/junction bypass attempts
        /// Updates the threat status in the history database upon successful restoration.
        /// </remarks>
        public async Task RestoreQuarantinedFile(string quarantinePath)
        {
            await Task.Run(async () =>
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

                    // Validate path before restoration to prevent path traversal attacks
                    if (!IsValidRestorePath(originalPath))
                    {
                        System.Diagnostics.Debug.WriteLine($"[QuarantineService] Invalid restore path blocked: {originalPath}");
                        throw new SecurityException($"Restoration to path '{originalPath}' is not allowed for security reasons.");
                    }

                    string? destDir = Path.GetDirectoryName(originalPath);
                    if (!string.IsNullOrEmpty(destDir)) Directory.CreateDirectory(destDir);

                    File.Move(quarantinePath, originalPath, overwrite: false);
                    if (File.Exists(metaPath)) File.Delete(metaPath);

                    // Update threat status in database
                    await _historyStore.UpdateThreatStatusAsync(originalPath, "Restored").ConfigureAwait(false);

                    System.Diagnostics.Debug.WriteLine($"[QuarantineService] Restored: {originalPath}");
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[QuarantineService] RestoreQuarantinedFile error: {ex.Message}");
                    throw;
                }
            });
        }

        /// <summary>
        /// Validates that a restore path is safe and not in a protected system directory.
        /// Prevents path traversal attacks and restoration to critical system folders.
        /// Resolves symbolic links and junction points to prevent bypasses.
        /// </summary>
        /// <param name="path">The path to validate</param>
        /// <returns>True if the path is safe for restoration, false otherwise</returns>
        /// <remarks>
        /// Security checks performed:
        /// - Resolves relative paths and traversal attempts (..)
        /// - Resolves symlinks and junction points on Windows
        /// - Blocks restoration to system directories (Windows, System32, Program Files, etc.)
        /// - Only allows restoration to user profile directories
        /// - Blocks paths containing suspicious patterns
        /// Conservative approach: blocks path if any validation step fails.
        /// </remarks>
        private bool IsValidRestorePath(string path)
        {
            if (string.IsNullOrEmpty(path) || path == "Unknown Path")
                return false;

            try
            {
                // Get full path to resolve any relative paths or traversal attempts (../)
                string fullPath = Path.GetFullPath(path);
                
                // On Windows, resolve reparse points (symlinks/junctions) to get real path
                if (OperatingSystem.IsWindows())
                {
                    try
                    {
                        var fileInfo = new FileInfo(fullPath);
                        // Check if it's a reparse point (symlink or junction)
                        if (fileInfo.Exists && fileInfo.Attributes.HasFlag(FileAttributes.ReparsePoint))
                        {
                            // Resolve the target of the symlink
                            string? linkTarget = fileInfo.LinkTarget;
                            if (!string.IsNullOrEmpty(linkTarget))
                            {
                                fullPath = Path.GetFullPath(linkTarget);
                                System.Diagnostics.Debug.WriteLine($"[QuarantineService] Resolved symlink: {path} -> {fullPath}");
                            }
                        }
                        
                        // Also check parent directories for reparse points
                        var dirInfo = fileInfo.Directory;
                        while (dirInfo != null)
                        {
                            if (dirInfo.Attributes.HasFlag(FileAttributes.ReparsePoint))
                            {
                                string? linkTarget = dirInfo.LinkTarget;
                                if (!string.IsNullOrEmpty(linkTarget))
                                {
                                    // Reconstruct full path with resolved directory
                                    string relativePath = Path.GetRelativePath(dirInfo.FullName, fullPath);
                                    fullPath = Path.GetFullPath(Path.Combine(linkTarget, relativePath));
                                    System.Diagnostics.Debug.WriteLine($"[QuarantineService] Resolved parent symlink: {fullPath}");
                                    break;
                                }
                            }
                            dirInfo = dirInfo.Parent;
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"[QuarantineService] Symlink resolution error: {ex.Message}");
                        // If we can't resolve symlinks, be conservative and block
                        return false;
                    }
                }
                
                // Block restoration to system directories
                string[] blockedPaths = new[]
                {
                    Environment.GetFolderPath(Environment.SpecialFolder.Windows),
                    Environment.GetFolderPath(Environment.SpecialFolder.System),
                    Environment.GetFolderPath(Environment.SpecialFolder.SystemX86),
                    Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                    Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                    Environment.GetFolderPath(Environment.SpecialFolder.CommonProgramFiles),
                    Environment.GetFolderPath(Environment.SpecialFolder.CommonProgramFilesX86),
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "System32"),
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "SysWOW64"),
                    Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData) // Block C:\ProgramData
                };

                // Check if path starts with any blocked directory
                foreach (var blockedPath in blockedPaths)
                {
                    if (!string.IsNullOrEmpty(blockedPath) && 
                        fullPath.StartsWith(blockedPath, StringComparison.OrdinalIgnoreCase))
                    {
                        System.Diagnostics.Debug.WriteLine($"[QuarantineService] Path blocked (system directory): {fullPath}");
                        return false;
                    }
                }

                // Only allow restoration to user directories
                string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                if (!string.IsNullOrEmpty(userProfile) && 
                    !fullPath.StartsWith(userProfile, StringComparison.OrdinalIgnoreCase))
                {
                    System.Diagnostics.Debug.WriteLine($"[QuarantineService] Path blocked (not in user profile): {fullPath}");
                    return false;
                }

                // Additional check: Ensure path doesn't contain suspicious patterns
                string normalizedPath = fullPath.Replace('/', '\\');
                if (normalizedPath.Contains("..\\") || normalizedPath.Contains(".."))
                {
                    System.Diagnostics.Debug.WriteLine($"[QuarantineService] Path blocked (traversal pattern): {fullPath}");
                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[QuarantineService] Path validation error: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Permanently deletes a quarantined file and its metadata.
        /// </summary>
        /// <param name="quarantinePath">The path to the quarantined file to delete</param>
        /// <returns>A task representing the asynchronous operation</returns>
        /// <remarks>
        /// Deletes both the .quarantine file and the .metadata file.
        /// This operation cannot be undone.
        /// </remarks>
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

        /// <summary>
        /// Purges quarantined files that are older than 30 days.
        /// </summary>
        /// <returns>A task representing the asynchronous operation</returns>
        /// <remarks>
        /// Automatically cleans up old quarantined files to prevent unbounded storage growth.
        /// Files are identified by their LastWriteTime timestamp.
        /// </remarks>
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

        /// <summary>
        /// Returns the paths of all files currently in quarantine.
        /// </summary>
        /// <returns>An enumerable collection of quarantine file paths</returns>
        /// <remarks>
        /// Returns only files with the .quarantine extension.
        /// Returns an empty collection if the quarantine directory doesn't exist or is inaccessible.
        /// </remarks>
        public IEnumerable<string> GetQuarantinedFiles()
        {
            string dir = _quarantinePath;
            if (!Directory.Exists(dir)) return Enumerable.Empty<string>();
            try   { return Directory.EnumerateFiles(dir, "*.quarantine"); }
            catch { return Enumerable.Empty<string>(); }
        }

        /// <summary>
        /// Returns the total quarantine directory size in megabytes.
        /// </summary>
        /// <returns>The total size of all quarantined files in MB</returns>
        /// <remarks>
        /// Calculates the sum of all .quarantine file sizes.
        /// Returns 0 if the directory doesn't exist or is inaccessible.
        /// </remarks>
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
