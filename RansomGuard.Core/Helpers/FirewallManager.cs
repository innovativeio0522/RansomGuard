using System;
using System.Diagnostics;
using System.Linq;
using System.Text;

namespace RansomGuard.Core.Helpers
{
    /// <summary>
    /// Manages Windows Firewall rules for RansomGuard features.
    /// </summary>
    public static class FirewallManager
    {
        private const string LanRuleName = "RansomGuard LAN Discovery";
        private const string LanRuleNameOutbound = "RansomGuard LAN Discovery (Outbound)";
        private const int DefaultLanPort = 47700;
        private const int NetshTimeoutMs = 10000;

        /// <summary>
        /// Ensures that firewall rules for LAN discovery are configured by an elevated service or setup process.
        /// The runtime service is the authoritative owner; installers/scripts may pre-provision the same rules.
        /// </summary>
        public static bool EnsureLanFirewallRules(int port = DefaultLanPort)
        {
            if (!OperatingSystem.IsWindows())
            {
                FileLogger.Log("firewall.log", "[Firewall] Skipping LAN rules on non-Windows OS.");
                return false;
            }

            if (port is < 1 or > 65535)
            {
                FileLogger.LogError("firewall.log", $"[Firewall] Invalid LAN port: {port}");
                return false;
            }

            try
            {
                if (!IsAdministrator())
                {
                    FileLogger.LogError("firewall.log", "[Firewall] LAN rules require elevated service/setup privileges.");
                    return false;
                }

                bool inboundReady = EnsureRule(LanRuleName, port, BuildInboundRuleArguments(port));
                bool outboundReady = EnsureRule(LanRuleNameOutbound, port, BuildOutboundRuleArguments(port));

                if (inboundReady && outboundReady)
                {
                    FileLogger.Log("firewall.log", $"[Firewall] LAN discovery rules verified for UDP port {port}.");
                    return true;
                }

                FileLogger.LogError("firewall.log", $"[Firewall] Failed to verify LAN discovery rules for UDP port {port}.");
                return false;
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
            if (!OperatingSystem.IsWindows())
                return false;

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
                var result = RunNetsh($"advfirewall firewall show rule name=\"{ruleName}\"");
                return result.ExitCode == 0 &&
                       result.Output.Contains("Rule Name:", StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        private static bool CheckRuleExistsForPort(string ruleName, int port)
        {
            try
            {
                var result = RunNetsh($"advfirewall firewall show rule name=\"{ruleName}\"");
                return result.ExitCode == 0 &&
                       result.Output.Contains("Rule Name:", StringComparison.OrdinalIgnoreCase) &&
                       RuleOutputMatchesPort(result.Output, port);
            }
            catch
            {
                return false;
            }
        }

        private static bool EnsureRule(string ruleName, int port, string addRuleArguments)
        {
            if (CheckRuleExistsForPort(ruleName, port))
            {
                FileLogger.Log("firewall.log", $"[Firewall] Rule already exists for UDP port {port}: {ruleName}");
                return true;
            }

            if (CheckRuleExists(ruleName))
            {
                FileLogger.Log("firewall.log", $"[Firewall] Replacing stale LAN rule for UDP port {port}: {ruleName}");
                DeleteRule(ruleName);
            }

            var result = RunNetsh(addRuleArguments);
            bool success = result.ExitCode == 0 &&
                           result.Output.Contains("Ok", StringComparison.OrdinalIgnoreCase);

            if (!success)
            {
                FileLogger.LogError(
                    "firewall.log",
                    $"[Firewall] Rule creation failed for '{ruleName}'. ExitCode={result.ExitCode}. Error={result.Error}");
            }

            return success;
        }

        internal static string BuildInboundRuleArguments(int port) =>
            $"advfirewall firewall add rule name=\"{LanRuleName}\" " +
            $"dir=in action=allow protocol=UDP localport={port} " +
            "profile=private,domain " +
            "description=\"Allows RansomGuard to discover and communicate with peers on the local network\"";

        internal static string BuildOutboundRuleArguments(int port) =>
            $"advfirewall firewall add rule name=\"{LanRuleNameOutbound}\" " +
            $"dir=out action=allow protocol=UDP localport={port} " +
            "profile=private,domain " +
            "description=\"Allows RansomGuard to broadcast discovery beacons on the local network\"";

        internal static bool RuleOutputMatchesPort(string output, int port)
        {
            var expectedPort = port.ToString(System.Globalization.CultureInfo.InvariantCulture);
            return output
                .Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries)
                .Any(line =>
                    line.Contains("LocalPort", StringComparison.OrdinalIgnoreCase) &&
                    line.Contains(expectedPort, StringComparison.OrdinalIgnoreCase));
        }

        private static NetshResult RunNetsh(string arguments)
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "netsh",
                    Arguments = arguments,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };

            var output = new StringBuilder();
            var error = new StringBuilder();
            process.OutputDataReceived += (_, e) => { if (e.Data != null) output.AppendLine(e.Data); };
            process.ErrorDataReceived += (_, e) => { if (e.Data != null) error.AppendLine(e.Data); };

            if (!process.Start())
                return new NetshResult(-1, string.Empty, "Failed to start netsh.");

            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            if (!process.WaitForExit(NetshTimeoutMs))
            {
                try { process.Kill(entireProcessTree: true); } catch { }
                return new NetshResult(-1, output.ToString(), "netsh timed out.");
            }

            process.WaitForExit();
            return new NetshResult(process.ExitCode, output.ToString(), error.ToString());
        }

        /// <summary>
        /// Deletes a firewall rule.
        /// </summary>
        private static bool DeleteRule(string ruleName)
        {
            try
            {
                var result = RunNetsh($"advfirewall firewall delete rule name=\"{ruleName}\"");
                return result.ExitCode == 0;
            }
            catch
            {
                return false;
            }
        }

        private readonly record struct NetshResult(int ExitCode, string Output, string Error);

        /// <summary>
        /// Checks if the current process has administrator privileges.
        /// </summary>
        public static bool IsAdministrator()
        {
            if (!OperatingSystem.IsWindows())
            {
                return false;
            }

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
