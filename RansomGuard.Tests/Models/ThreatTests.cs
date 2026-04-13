using RansomGuard.Core.Models;
using FluentAssertions;
using Xunit;

namespace RansomGuard.Tests.Models;

public class ThreatTests
{
    [Fact]
    public void Threat_ShouldInitializeWithDefaultValues()
    {
        // Arrange & Act
        var threat = new Threat();

        // Assert
        threat.Name.Should().BeEmpty();
        threat.Path.Should().BeEmpty();
        threat.ProcessName.Should().BeEmpty();
        threat.Severity.Should().Be(ThreatSeverity.Low);
        threat.Timestamp.Should().BeCloseTo(DateTime.Now, TimeSpan.FromSeconds(1));
    }

    [Fact]
    public void Threat_ShouldAllowSettingProperties()
    {
        // Arrange
        var threat = new Threat
        {
            Name = "Test Threat",
            Path = "C:\\test\\file.exe",
            ProcessName = "malware.exe",
            Severity = ThreatSeverity.Critical,
            Timestamp = DateTime.Now
        };

        // Assert
        threat.Name.Should().Be("Test Threat");
        threat.Path.Should().Be("C:\\test\\file.exe");
        threat.ProcessName.Should().Be("malware.exe");
        threat.Severity.Should().Be(ThreatSeverity.Critical);
    }

    [Theory]
    [InlineData(ThreatSeverity.Low)]
    [InlineData(ThreatSeverity.Medium)]
    [InlineData(ThreatSeverity.High)]
    [InlineData(ThreatSeverity.Critical)]
    public void Threat_ShouldAcceptAllSeverityLevels(ThreatSeverity severity)
    {
        // Arrange & Act
        var threat = new Threat { Severity = severity };

        // Assert
        threat.Severity.Should().Be(severity);
    }

    [Fact]
    public void Threat_ActionTaken_ShouldBeSettable()
    {
        // Arrange
        var threat = new Threat();

        // Act
        threat.ActionTaken = "Quarantined";

        // Assert
        threat.ActionTaken.Should().Be("Quarantined");
    }
}
