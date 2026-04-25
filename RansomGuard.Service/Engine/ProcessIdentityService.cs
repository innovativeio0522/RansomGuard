using System;
using System.Diagnostics;
using RansomGuard.Core.Services;

namespace RansomGuard.Service.Engine
{
    /// <summary>
    /// Determines whether a running process should be considered trusted or suspicious
    /// based on its name, path, digital signature, and the user whitelist.
    /// Extracted from SentinelEngine to reduce class size (#29).
    /// </summary>
    /// <remarks>
    /// Uses a layered heuristic approach:
    /// 1. User whitelist (highest priority)
    /// 2. Critical OS process names
    /// 3. Path-based heuristics with Authenticode verification
    /// 4. Fallback name matching for common safe processes
    /// </remarks>
    internal class ProcessIdentityService : IProcessIdentityClassifier
    {
        private readonly IAuthenticodeVerifier _verifier;
        private readonly System.Collections.Concurrent.ConcurrentDictionary<string, (bool IsTrusted, string Status, DateTime Expires)> _signatureCache = new();
        private const int Windows11BuildNumber = 20348;
        private const int CacheExpirationHours = 24;

        public ProcessIdentityService(IAuthenticodeVerifier? verifier = null)
        {
            _verifier = verifier ?? new AuthenticodeVerifier();
        }

        /// <summary>
        /// Classifies a process as trusted/untrusted and assigns a human-readable status label.
        /// Uses a layered heuristic: user whitelist → critical system names → executable path → fallback names.
        /// </summary>
        /// <param name="p">The process to classify</param>
        /// <returns>
        /// A tuple of (IsTrusted, StatusLabel) where:
        /// - IsTrusted: true if the process is considered safe, false otherwise
        /// - StatusLabel: Human-readable description of the trust decision
        /// </returns>
        /// <remarks>
        /// This method handles various edge cases including:
        /// - Processes that have already exited
        /// - Protected system processes that deny access
        /// - Processes with missing or inaccessible executable paths
        /// - Digital signature verification with caching
        /// </remarks>
        public (bool IsTrusted, string Status) DetermineIdentity(Process p)
        {
            try
            {
                // Check if process has exited before accessing properties
                if (p.HasExited)
                    return (false, "Process Exited");

                string nameLower = p.ProcessName.ToLowerInvariant();

                // 1. User Whitelist (highest priority — explicit user trust)
                if (ConfigurationService.Instance.IsProcessWhitelisted(p.ProcessName))
                    return (true, "User Whitelisted");

                // 2. Critical OS process names baseline (Security fallback for fundamental system objects)
                if (nameLower is "system" or "idle")
                    return (true, "System Verified");

                if (nameLower.Contains("ransomguard", StringComparison.OrdinalIgnoreCase))
                    return (true, "RansomGuard Self-Check");

                // 3. Path-based heuristics (requires elevated access for most processes)
                try
                {
                    // Check again if process exited during execution
                    if (p.HasExited)
                        return (false, "Process Exited");

                    string? path = p.MainModule?.FileName?.ToLowerInvariant();
                    if (!string.IsNullOrEmpty(path))
                    {
                        // --- NEW: Authenticode Signature Verification ---
                        if (_signatureCache.TryGetValue(path, out var cachedResult))
                        {
                            if (DateTime.Now < cachedResult.Expires)
                                return (cachedResult.IsTrusted, cachedResult.Status);
                            
                            _signatureCache.TryRemove(path, out _);
                        }

                        if (_verifier.IsMicrosoftSigned(path))
                        {
                            var result = (true, "OS Component (Verified)");
                            _signatureCache[path] = (result.Item1, result.Item2, DateTime.Now.AddHours(CacheExpirationHours));
                            return result;
                        }

                        // Check for other trusted publishers if needed
                        string publisher = _verifier.GetPublisher(path);
                        if (!string.IsNullOrEmpty(publisher) && publisher != "Unsigned")
                        {
                            var result = (true, "Trusted Application");
                            _signatureCache[path] = (result.Item1, result.Item2, DateTime.Now.AddHours(CacheExpirationHours));
                            return result;
                        }
                        // ------------------------------------------------

                        if (path.Contains(@"c:\windows\", StringComparison.OrdinalIgnoreCase))
                            return (true, "OS Component (Path-Only)");

                        if (path.Contains(@"c:\program files\", StringComparison.OrdinalIgnoreCase) || 
                            path.Contains(@"c:\program files (x86)\", StringComparison.OrdinalIgnoreCase))
                            return (true, "Installed Application");

                        if (path.Contains(@"\appdata\local\", StringComparison.OrdinalIgnoreCase) || 
                            path.Contains(@"\appdata\roaming\", StringComparison.OrdinalIgnoreCase))
                        {
                            if (nameLower is "code" or "brave" or "dotnet" or "node" or "git"
                                or "chrome" or "ms-teams" or "discord" or "antigravity"
                                or "msbuild" or "csc" or "cl" or "link" or "ninja"
                                || nameLower.Contains("language_server", StringComparison.OrdinalIgnoreCase))
                                return (true, "User Verified");
                        }

                        if (path.Contains(@"\programdata\microsoft\windows defender\", StringComparison.OrdinalIgnoreCase))
                            return (true, "Security Component");
                    }
                }
                catch (System.ComponentModel.Win32Exception ex)
                {
                    // Access denied even for SYSTEM on some protected processes (e.g., smss.exe)
                    Debug.WriteLine($"[ProcessIdentity] Access denied for PID {p.Id} ({nameLower}): {ex.Message}");
                    
                    // If we can't get the path, we can't verify the signature. 
                    // Fall back to a very restrictive name-check for known critical processes.
                    if (nameLower is "smss" or "csrss" or "wininit" or "winlogon" or "lsass")
                        return (true, "System Component (Protected)");
                }
                catch (InvalidOperationException ex)
                {
                    // Process exited while we were accessing it
                    Debug.WriteLine($"[ProcessIdentity] Process exited during access: {ex.Message}");
                    return (false, "Process Exited");
                }

                // 4. Fallback name matching for common safe processes when path is inaccessible
                if (nameLower is "dotnet" or "code" or "brave" or "explorer" or "antigravity" or "msmpeng"
                    or "msbuild" or "csc" or "git" or "cl" or "link" or "ninja"
                    || nameLower.Contains("mbam", StringComparison.OrdinalIgnoreCase) 
                    || nameLower.Contains("malwarebytes", StringComparison.OrdinalIgnoreCase))
                    return (true, "User Verified");

                return (false, "Unknown Issuer");
            }
            catch (InvalidOperationException)
            {
                // Process exited
                return (false, "Process Exited");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ProcessIdentity] Unexpected error: {ex.Message}");
                return (false, "Verification Error");
            }
        }

        /// <summary>
        /// Attempts to find which processes are currently interacting with a file.
        /// </summary>
        /// <param name="path">The file path to check</param>
        /// <returns>A list of processes that have handles open to the specified file</returns>
        /// <remarks>
        /// This method uses the FileOwnershipResolver to enumerate processes with open handles.
        /// May return an empty list if no processes are found or if access is denied.
        /// </remarks>
        public System.Collections.Generic.List<Process> GetProcessesUsingFile(string path)
        {
            return FileOwnershipResolver.GetProcessesUsingFile(path);
        }
    }
}
