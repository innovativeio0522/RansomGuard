using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Win32;

namespace RansomGuard.Core.Helpers
{
    /// <summary>
    /// Provides centralized path configuration for RansomGuard application directories.
    /// Automatically detects MSIX packaging and uses appropriate storage locations.
    /// </summary>
    public static class PathConfiguration
    {
        private static readonly string[] StandardProtectedSubFolders =
        {
            "Documents",
            "Desktop",
            "Pictures",
            "Music",
            "Videos",
            "Downloads",
            "OneDrive"
        };

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern int GetCurrentPackageFullName(ref int packageFullNameLength, System.Text.StringBuilder? packageFullName);

        /// <summary>
        /// Detects if the application is running as an MSIX package using the Win32 API.
        /// This is more reliable than checking environment variables.
        /// </summary>
        private static bool IsRunningAsMsix()
        {
            try
            {
                int length = 0;
                int result = GetCurrentPackageFullName(ref length, null);
                // ERROR_INSUFFICIENT_BUFFER (122) means we have a package name — we're MSIX
                // ERROR_NOT_FOUND (15100) means no package — traditional install
                return result == 122; // ERROR_INSUFFICIENT_BUFFER
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Gets the base directory for application data.
        /// - User processes (UI): ProgramData\RGCoreEssentials (Shared with Service)
        /// - System service (LocalSystem): ProgramData\RGCoreEssentials
        /// </summary>
        private static readonly string BaseDirectory = GetBaseDirectory();

        private static string GetBaseDirectory()
        {
            // FORCE ProgramData for both UI and Service to ensure synchronization.
            var programData = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                Configuration.AppConstants.General.AppName
            );
            
            return programData;
        }

        /// <summary>
        /// Gets the path to the quarantine directory where isolated suspicious files are stored.
        /// </summary>
        public static string QuarantinePath => Path.Combine(BaseDirectory, "Quarantine");
        
        /// <summary>
        /// Gets the path to the honey pot directory for bait files used to detect ransomware activity.
        /// </summary>
        public static string HoneyPotPath => Path.Combine(BaseDirectory, "HoneyPots");
        
        /// <summary>
        /// Gets the path to the log directory for application logs and diagnostic information.
        /// </summary>
        public static string LogPath => Path.Combine(BaseDirectory, "Logs");

        /// <summary>
        /// Gets the path to the activity log database.
        /// </summary>
        public static string ActivityLogDatabasePath => Path.Combine(BaseDirectory, "activity_log.db");
        
        /// <summary>
        /// Gets the configuration directory path.
        /// </summary>
        public static string GetConfigDirectory() => BaseDirectory;

        /// <summary>
        /// Returns the root of all user profiles on this machine (typically C:\Users).
        /// Uses registry to get the correct path — safe to call from Session 0 (LocalSystem).
        /// </summary>
        [SupportedOSPlatform("windows")]
        public static string GetUsersRootPath()
        {
            try
            {
                // Read from registry — works correctly in Session 0, unlike environment variables
                // HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\ProfileList\ProfilesDirectory
#pragma warning disable CA1416 // Validate platform compatibility - RansomGuard is Windows-only
                using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(
                    @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\ProfileList");
                var profilesDir = key?.GetValue("ProfilesDirectory") as string;
#pragma warning restore CA1416
                if (!string.IsNullOrEmpty(profilesDir))
                {
                    profilesDir = Environment.ExpandEnvironmentVariables(profilesDir);
                    if (Directory.Exists(profilesDir))
                        return profilesDir;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[PathConfiguration] Registry read failed: {ex.Message}");
            }

            // Fallback: derive from SystemDrive environment variable (safe in Session 0)
            var systemDrive = Environment.GetEnvironmentVariable("SystemDrive") ?? "C:";
            // Ensure uppercase drive letter for FileSystemWatcher bug
            if (systemDrive.Length > 0)
            {
                systemDrive = systemDrive.Substring(0, 1).ToUpperInvariant() + systemDrive.Substring(1);
            }
            return Path.Combine(systemDrive, "Users");
        }

        /// <summary>
        /// Returns the standard protected folders for ALL user profiles on this machine.
        /// These are always watched regardless of what is in config.json.
        /// Includes: Documents, Desktop, Pictures, Music, Videos, Downloads, OneDrive.
        /// </summary>
        [SupportedOSPlatform("windows")]
        public static IEnumerable<string> GetAllUsersStandardFolders()
        {
            var folders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            try
            {
                string usersRoot = GetUsersRootPath();
                if (!Directory.Exists(usersRoot)) return folders;

#pragma warning disable CA1416 // Validate platform compatibility - RansomGuard is Windows-only
                using var profileListKey = Registry.LocalMachine.OpenSubKey(
                    @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\ProfileList");
#pragma warning restore CA1416
                if (profileListKey == null) return folders;

                foreach (var profileSid in profileListKey.GetSubKeyNames())
                {
                    try
                    {
#pragma warning disable CA1416 // Validate platform compatibility - RansomGuard is Windows-only
                        using var profileKey = profileListKey.OpenSubKey(profileSid);
#pragma warning restore CA1416
                        var profilePath = profileKey?.GetValue("ProfileImagePath") as string;

                        if (!ShouldIncludeProfileDirectory(usersRoot, profileSid, profilePath))
                            continue;

                        var userDir = NormalizeDirectoryPath(Environment.ExpandEnvironmentVariables(profilePath!));
                        foreach (var sub in StandardProtectedSubFolders)
                        {
                            var candidate = Path.Combine(userDir, sub);
                            if (Directory.Exists(candidate))
                                folders.Add(candidate);
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine(
                            $"[PathConfiguration] Skipping profile SID '{profileSid}': {ex.Message}");
                        continue;
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[PathConfiguration] GetAllUsersStandardFolders error: {ex.Message}");
            }

            return folders;
        }

        internal static bool IsRealUserProfileSid(string? sid)
        {
            if (string.IsNullOrWhiteSpace(sid))
                return false;

            // Standard local/domain users are S-1-5-21-... and Azure AD / Microsoft Entra users are S-1-12-1-...
            return sid.StartsWith("S-1-5-21-", StringComparison.OrdinalIgnoreCase) ||
                   sid.StartsWith("S-1-12-1-", StringComparison.OrdinalIgnoreCase);
        }

        internal static bool ShouldIncludeProfileDirectory(string usersRoot, string profileSid, string? profilePath)
        {
            if (!IsRealUserProfileSid(profileSid) || string.IsNullOrWhiteSpace(profilePath))
                return false;

            var normalizedUsersRoot = NormalizeDirectoryPath(usersRoot);
            var normalizedProfilePath = NormalizeDirectoryPath(Environment.ExpandEnvironmentVariables(profilePath));

            if (!Directory.Exists(normalizedUsersRoot) || !Directory.Exists(normalizedProfilePath))
                return false;

            var relativeProfilePath = Path.GetRelativePath(normalizedUsersRoot, normalizedProfilePath);
            if (relativeProfilePath.StartsWith("..", StringComparison.OrdinalIgnoreCase) ||
                Path.IsPathRooted(relativeProfilePath))
                return false;

            var dirName = Path.GetFileName(normalizedProfilePath);
            if (string.IsNullOrWhiteSpace(dirName))
                return false;

            return !string.Equals(dirName, "Public", StringComparison.OrdinalIgnoreCase) &&
                   !string.Equals(dirName, "Default", StringComparison.OrdinalIgnoreCase) &&
                   !string.Equals(dirName, "Default User", StringComparison.OrdinalIgnoreCase) &&
                   !string.Equals(dirName, "All Users", StringComparison.OrdinalIgnoreCase);
        }

        private static string NormalizeDirectoryPath(string path) =>
            Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        
        /// <summary>
        /// Ensures all required application directories exist, creating them if necessary.
        /// </summary>
        public static void EnsureDirectoriesExist()
        {
            try
            {
                Directory.CreateDirectory(QuarantinePath);
                Directory.CreateDirectory(HoneyPotPath);
                Directory.CreateDirectory(LogPath);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to create directories: {ex.Message}");
            }
        }
        
        static PathConfiguration()
        {
            EnsureDirectoriesExist();
        }
    }
}
