using RansomGuard.Core.Helpers;
using RansomGuard.Core.Configuration;
using FluentAssertions;
using System.IO;
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
    public void QuarantinePath_ShouldContainConfiguredAppName()
    {
        // Arrange & Act
        var path = PathConfiguration.QuarantinePath;

        // Assert
        path.Should().Contain(AppConstants.General.AppName);
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

    [Theory]
    [InlineData("S-1-5-21-111111111-222222222-333333333-1001")]
    [InlineData("S-1-12-1-123456789-987654321-456789123-1122334455")]
    public void IsRealUserProfileSid_ShouldReturnTrueForInteractiveUserSids(string sid)
    {
        PathConfiguration.IsRealUserProfileSid(sid).Should().BeTrue();
    }

    [Theory]
    [InlineData("")]
    [InlineData("S-1-5-18")]
    [InlineData("S-1-5-19")]
    [InlineData("S-1-5-20")]
    [InlineData("S-1-5-80-12345")]
    [InlineData("S-1-5-82-12345")]
    public void IsRealUserProfileSid_ShouldReturnFalseForServiceOrVirtualAccountSids(string sid)
    {
        PathConfiguration.IsRealUserProfileSid(sid).Should().BeFalse();
    }

    [Fact]
    public void ShouldIncludeProfileDirectory_ShouldReturnTrueForRealProfileUnderUsersRoot()
    {
        var testRoot = Path.Combine(Path.GetTempPath(), "RG_PathConfig_" + Guid.NewGuid().ToString("N"));
        var usersRoot = Path.Combine(testRoot, "Users");
        var profilePath = Path.Combine(usersRoot, "TestUser");

        try
        {
            Directory.CreateDirectory(profilePath);

            PathConfiguration.ShouldIncludeProfileDirectory(
                usersRoot,
                "S-1-5-21-111111111-222222222-333333333-1001",
                profilePath).Should().BeTrue();
        }
        finally
        {
            if (Directory.Exists(testRoot))
                Directory.Delete(testRoot, recursive: true);
        }
    }

    [Fact]
    public void ShouldIncludeProfileDirectory_ShouldReturnFalseForPseudoProfileNamesOrPathsOutsideUsersRoot()
    {
        var testRoot = Path.Combine(Path.GetTempPath(), "RG_PathConfig_" + Guid.NewGuid().ToString("N"));
        var usersRoot = Path.Combine(testRoot, "Users");
        var defaultProfile = Path.Combine(usersRoot, "Default");
        var externalProfile = Path.Combine(testRoot, "ExternalUser");

        try
        {
            Directory.CreateDirectory(defaultProfile);
            Directory.CreateDirectory(externalProfile);

            PathConfiguration.ShouldIncludeProfileDirectory(
                usersRoot,
                "S-1-5-21-111111111-222222222-333333333-1001",
                defaultProfile).Should().BeFalse();

            PathConfiguration.ShouldIncludeProfileDirectory(
                usersRoot,
                "S-1-5-21-111111111-222222222-333333333-1001",
                externalProfile).Should().BeFalse();
        }
        finally
        {
            if (Directory.Exists(testRoot))
                Directory.Delete(testRoot, recursive: true);
        }
    }
}
