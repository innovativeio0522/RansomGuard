using FluentAssertions;
using RansomGuard.Core.Helpers;
using Xunit;

namespace RansomGuard.Tests.Core;

public class FirewallManagerTests
{
    [Fact]
    public void BuildInboundRuleArguments_ShouldUseConfiguredLanPort()
    {
        var args = FirewallManager.BuildInboundRuleArguments(47888);

        args.Should().Contain("name=\"RansomGuard LAN Discovery\"");
        args.Should().Contain("dir=in");
        args.Should().Contain("protocol=UDP");
        args.Should().Contain("localport=47888");
        args.Should().Contain("profile=private,domain");
    }

    [Fact]
    public void BuildOutboundRuleArguments_ShouldUseConfiguredLanPort()
    {
        var args = FirewallManager.BuildOutboundRuleArguments(47888);

        args.Should().Contain("name=\"RansomGuard LAN Discovery (Outbound)\"");
        args.Should().Contain("dir=out");
        args.Should().Contain("protocol=UDP");
        args.Should().Contain("localport=47888");
        args.Should().Contain("profile=private,domain");
    }

    [Fact]
    public void RuleOutputMatchesPort_ShouldRequireMatchingLocalPort()
    {
        var output = """
            Rule Name:                            RansomGuard LAN Discovery
            Enabled:                              Yes
            Direction:                            In
            Profiles:                             Domain,Private
            Protocol:                             UDP
            LocalPort:                            47888
            """;

        FirewallManager.RuleOutputMatchesPort(output, 47888).Should().BeTrue();
        FirewallManager.RuleOutputMatchesPort(output, 47700).Should().BeFalse();
    }
}
