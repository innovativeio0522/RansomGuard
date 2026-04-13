using RansomGuard.Core.Helpers;
using FluentAssertions;
using Xunit;

namespace RansomGuard.Tests.Core;

public class PathConfigurationTests
{
    [Fact]
    public void QuarantinePath_ShouldNotBeNullOrEmpty()
    {
        // Arrange & Act
        var path = PathConfiguration.QuarantinePath;

        // Assert
        path.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void QuarantinePath_ShouldContainRansomGuard()
    {
        // Arrange & Act
        var path = PathConfiguration.QuarantinePath;

        // Assert
        path.Should().Contain("RansomGuard");
    }

    [Fact]
    public void QuarantinePath_ShouldContainQuarantine()
    {
        // Arrange & Act
        var path = PathConfiguration.QuarantinePath;

        // Assert
        path.Should().Contain("Quarantine");
    }

    [Fact]
    public void HoneyPotPath_ShouldNotBeNullOrEmpty()
    {
        // Arrange & Act
        var path = PathConfiguration.HoneyPotPath;

        // Assert
        path.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void HoneyPotPath_ShouldContainHoneyPots()
    {
        // Arrange & Act
        var path = PathConfiguration.HoneyPotPath;

        // Assert
        path.Should().Contain("HoneyPots");
    }

    [Fact]
    public void LogPath_ShouldNotBeNullOrEmpty()
    {
        // Arrange & Act
        var path = PathConfiguration.LogPath;

        // Assert
        path.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void LogPath_ShouldContainLogs()
    {
        // Arrange & Act
        var path = PathConfiguration.LogPath;

        // Assert
        path.Should().Contain("Logs");
    }

    [Fact]
    public void EnsureDirectoriesExist_ShouldNotThrowException()
    {
        // Arrange & Act
        Action act = () => PathConfiguration.EnsureDirectoriesExist();

        // Assert
        act.Should().NotThrow();
    }

    [Fact]
    public void AllPaths_ShouldBeDifferent()
    {
        // Arrange & Act
        var quarantinePath = PathConfiguration.QuarantinePath;
        var honeyPotPath = PathConfiguration.HoneyPotPath;
        var logPath = PathConfiguration.LogPath;

        // Assert
        quarantinePath.Should().NotBe(honeyPotPath);
        quarantinePath.Should().NotBe(logPath);
        honeyPotPath.Should().NotBe(logPath);
    }
}
