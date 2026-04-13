using RansomGuard.Core.Models;
using FluentAssertions;
using Xunit;

namespace RansomGuard.Tests.Models;

public class ProcessInfoTests
{
    [Fact]
    public void ProcessInfo_ShouldInitializeWithDefaultValues()
    {
        // Arrange & Act
        var processInfo = new ProcessInfo();

        // Assert
        processInfo.Pid.Should().Be(0);
        processInfo.Name.Should().BeEmpty();
        processInfo.CpuUsage.Should().Be(0);
        processInfo.MemoryUsage.Should().Be(0);
    }

    [Fact]
    public void ProcessInfo_ShouldAllowSettingProperties()
    {
        // Arrange
        var processInfo = new ProcessInfo
        {
            Pid = 1234,
            Name = "chrome.exe",
            CpuUsage = 15.5,
            MemoryUsage = 524288000 // ~500MB
        };

        // Assert
        processInfo.Pid.Should().Be(1234);
        processInfo.Name.Should().Be("chrome.exe");
        processInfo.CpuUsage.Should().Be(15.5);
        processInfo.MemoryUsage.Should().Be(524288000);
    }

    [Fact]
    public void ProcessInfo_CpuUsage_ShouldAcceptValidRange()
    {
        // Arrange
        var processInfo = new ProcessInfo();

        // Act
        processInfo.CpuUsage = 50.0;

        // Assert
        processInfo.CpuUsage.Should().BeInRange(0, 100);
    }

    [Fact]
    public void ProcessInfo_MemoryUsage_ShouldBePositive()
    {
        // Arrange
        var processInfo = new ProcessInfo { MemoryUsage = 1024000 };

        // Assert
        processInfo.MemoryUsage.Should().BeGreaterThan(0);
    }
}
