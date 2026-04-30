using System;
using System.Diagnostics;

namespace RansomGuard.Core.Helpers
{
    /// <summary>
    /// Manages Windows Firewall rules for RansomGuard features.
    /// </summary>
    public static class FirewallManager
    {
        private const string LanRuleName = "RansomGuard LAN Discovery";
        private const string LanRuleNameOutbound = "RansomGuard LAN Discovery (Outbound)";
        private const int LanPort = 47700;

        /// <summary>
        /// Ensures that firewall rules for LAN discovery are configured.
        /// Returns true if rules were created/verified, false if admin privileges are required.
        /// </summary>
        public static bool EnsureLanFirewallRules()
        {
            try
            {
                // Check if rules already exist
                if (CheckRuleExists(LanRuleName) && CheckRuleExists(LanRuleNameOutbound))
                {
                    FileLogger.Log("firewall.log", "[Firewall] LAN discovery rules already exist.");
                    return true;
                }

                // Try to create the rules
                bool inboundCreated = CreateInboundRule();
                bool outboundCreated = CreateOutboundRule();

                if (inboundCreated && outboundCreated)
                {
                    FileLogger.Log("firewall.log", "[Firewall] LAN discovery rules created successfully.");
                    return true;
                }
                else
                {
                    FileLogger.LogError("firewall.log", "[Firewall] Failed to create rules - administrator privileges may be required.");
                    return false;
                }
            }
            catch (Exception ex)
            {
                FileLogger.LogError("firewall.log", $"[Firewall] Error ensuring LAN rules: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Removes firewall rules for LAN discovery.
        /// </summary>
        public static bool RemoveLanFirewallRules()
        {
            try
            {
                bool inboundRemoved = DeleteRule(LanRuleName);
                bool outboundRemoved = DeleteRule(LanRuleNameOutbound);

                if (inboundRemoved || outboundRemoved)
                {
                    FileLogger.Log("firewall.log", "[Firewall] LAN discovery rules removed.");
                    return true;
                }

                return false;
            }
            catch (Exception ex)
            {
                FileLogger.LogError("firewall.log", $"[Firewall] Error removing LAN rules: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Checks if a firewall rule exists.
        /// </summary>
        private static bool CheckRuleExists(string ruleName)
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "netsh",
                    Arguments = $"advfirewall firewall show rule name=\"{ruleName}\"",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using var process = Process.Start(psi);
                if (process == null) return false;

                string output = process.StandardOutput.ReadToEnd();
                process.WaitForExit();

                // If rule exists, output contains "Rule Name:"
                return output.Contains("Rule Name:", StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Creates the inbound firewall rule for LAN discovery.
        /// </summary>
        private static bool CreateInboundRule()
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "netsh",
                    Arguments = $"advfirewall firewall add rule name=\"{LanRuleName}\" " +
                               $"dir=in action=allow protocol=UDP localport={LanPort} " +
                               $"profile=private,domain " +
                               $"description=\"Allows RansomGuard to discover and communicate with peers on the local network\"",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    Verb = "runas" // Request elevation
                };

                using var process = Process.Start(psi);
                if (process == null) return false;

                string output = process.StandardOutput.ReadToEnd();
                string error = process.StandardError.ReadToEnd();
                process.WaitForExit();

                bool success = process.ExitCode == 0 && output.Contains("Ok", StringComparison.OrdinalIgnoreCase);
                
                if (!success && !string.IsNullOrEmpty(error))
                {
                    FileLogger.LogError("firewall.log", $"[Firewall] Inbound rule creation error: {error}");
                }

                return success;
            }
            catch (Exception ex)
            {
                FileLogger.LogError("firewall.log", $"[Firewall] Exception creating inbound rule: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Creates the outbound firewall rule for LAN discovery.
        /// </summary>
        private static bool CreateOutboundRule()
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "netsh",
                    Arguments = $"advfirewall firewall add rule name=\"{LanRuleNameOutbound}\" " +
                               $"dir=out action=allow protocol=UDP localport={LanPort} " +
                               $"profile=private,domain " +
                               $"description=\"Allows RansomGuard to broadcast discovery beacons on the local network\"",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    Verb = "runas" // Request elevation
                };

                using var process = Process.Start(psi);
                if (process == null) return false;

                string output = process.StandardOutput.ReadToEnd();
                string error = process.StandardError.ReadToEnd();
                process.WaitForExit();

                bool success = process.ExitCode == 0 && output.Contains("Ok", StringComparison.OrdinalIgnoreCase);
                
                if (!success && !string.IsNullOrEmpty(error))
                {
                    FileLogger.LogError("firewall.log", $"[Firewall] Outbound rule creation error: {error}");
                }

                return success;
            }
            catch (Exception ex)
            {
                FileLogger.LogError("firewall.log", $"[Firewall] Exception creating outbound rule: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Deletes a firewall rule.
        /// </summary>
        private static bool DeleteRule(string ruleName)
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "netsh",
                    Arguments = $"advfirewall firewall delete rule name=\"{ruleName}\"",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using var process = Process.Start(psi);
                if (process == null) return false;

                process.WaitForExit();
                return process.ExitCode == 0;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Checks if the current process has administrator privileges.
        /// </summary>
        public static bool IsAdministrator()
        {
            try
            {
                var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
                var principal = new System.Security.Principal.WindowsPrincipal(identity);
                return principal.IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
            }
            catch
            {
                return false;
            }
        }
    }
}
