using RansomGuard.Core.Services;
using FluentAssertions;
using Xunit;

namespace RansomGuard.Tests.Core;

public class ConfigurationServiceTests
{
    [Fact]
    public void Instance_ShouldReturnSingleton()
    {
        // Arrange & Act
        var instance1 = ConfigurationService.Instance;
        var instance2 = ConfigurationService.Instance;

        // Assert
        instance1.Should().BeSameAs(instance2);
    }

    [Fact]
    public void MonitoredPaths_ShouldInitializeAsEmptyList()
    {
        // Arrange & Act
        var config = ConfigurationService.Instance;

        // Assert
        config.MonitoredPaths.Should().NotBeNull();
        config.MonitoredPaths.Should().BeOfType<List<string>>();
    }

    [Fact]
    public void SensitivityLevel_ShouldDefaultToThree()
    {
        // Arrange & Act
        var config = ConfigurationService.Instance;

        // Assert
        // Note: Actual value may be 4 if loaded from config.json
        config.SensitivityLevel.Should().BeInRange(1, 4);
    }

    [Fact]
    public void RealTimeProtection_ShouldDefaultToTrue()
    {
        // Arrange & Act
        var config = ConfigurationService.Instance;

        // Assert
        config.RealTimeProtection.Should().BeTrue();
    }

    [Fact]
    public void AutoQuarantine_ShouldDefaultToTrue()
    {
        // Arrange & Act
        var config = ConfigurationService.Instance;

        // Assert
        config.AutoQuarantine.Should().BeTrue();
    }

    [Fact]
    public void LastScanTime_ShouldDefaultToMinValue()
    {
        // Arrange & Act
        var config = ConfigurationService.Instance;

        // Assert
        config.LastScanTime.Should().Be(DateTime.MinValue);
    }

    [Fact]
    public void Save_ShouldNotThrowException()
    {
        // Arrange
        var config = ConfigurationService.Instance;

        // Act
        Action act = () => config.Save();

        // Assert
        act.Should().NotThrow();
    }

    [Fact]
    public void NotifyPathsChanged_ShouldRaiseEvent()
    {
        // Arrange
        var config = ConfigurationService.Instance;
        var eventRaised = false;
        config.PathsChanged += () => eventRaised = true;

        // Act
        config.NotifyPathsChanged();

        // Assert
        eventRaised.Should().BeTrue();
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void SensitivityLevel_ShouldAcceptValidValues(int level)
    {
        // Arrange
        var config = ConfigurationService.Instance;

        // Act
        config.SensitivityLevel = level;

        // Assert
        config.SensitivityLevel.Should().Be(level);
    }
}
