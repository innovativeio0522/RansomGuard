using RansomGuard.Core.Helpers;
using FluentAssertions;
using Xunit;

namespace RansomGuard.Tests.Core;

public class NativeMemoryTests
{
    [Fact]
    public void GetTotalPhysicalMemoryMb_ShouldReturnPositiveValue()
    {
        // Arrange & Act
        var totalMemory = NativeMemory.GetTotalPhysicalMemoryMb();

        // Assert
        totalMemory.Should().BeGreaterThan(0);
    }

    [Fact]
    public void GetAvailablePhysicalMemoryMb_ShouldReturnPositiveValue()
    {
        // Arrange & Act
        var availableMemory = NativeMemory.GetAvailablePhysicalMemoryMb();

        // Assert
        availableMemory.Should().BeGreaterOrEqualTo(0);
    }

    [Fact]
    public void GetUsedPhysicalMemoryMb_ShouldReturnPositiveValue()
    {
        // Arrange & Act
        var usedMemory = NativeMemory.GetUsedPhysicalMemoryMb();

        // Assert
        usedMemory.Should().BeGreaterOrEqualTo(0);
    }

    [Fact]
    public void GetMemoryStatus_ShouldReturnTrue()
    {
        // Arrange & Act
        var result = NativeMemory.GetMemoryStatus(out var memStatus);

        // Assert
        result.Should().BeTrue();
        memStatus.Should().NotBeNull();
    }

    [Fact]
    public void GetMemoryStatus_ShouldHaveValidTotalPhysicalMemory()
    {
        // Arrange & Act
        NativeMemory.GetMemoryStatus(out var memStatus);

        // Assert
        memStatus.ullTotalPhys.Should().BeGreaterThan(0);
    }

    [Fact]
    public void UsedMemory_ShouldBeLessThanTotalMemory()
    {
        // Arrange & Act
        var totalMemory = NativeMemory.GetTotalPhysicalMemoryMb();
        var usedMemory = NativeMemory.GetUsedPhysicalMemoryMb();

        // Assert
        usedMemory.Should().BeLessThanOrEqualTo(totalMemory);
    }

    [Fact]
    public void AvailableMemory_ShouldBeLessThanTotalMemory()
    {
        // Arrange & Act
        var totalMemory = NativeMemory.GetTotalPhysicalMemoryMb();
        var availableMemory = NativeMemory.GetAvailablePhysicalMemoryMb();

        // Assert
        availableMemory.Should().BeLessThanOrEqualTo(totalMemory);
    }

    [Fact]
    public void UsedPlusAvailable_ShouldEqualTotal()
    {
        // Arrange & Act
        var totalMemory = NativeMemory.GetTotalPhysicalMemoryMb();
        var usedMemory = NativeMemory.GetUsedPhysicalMemoryMb();
        var availableMemory = NativeMemory.GetAvailablePhysicalMemoryMb();

        // Assert
        var sum = usedMemory + availableMemory;
        sum.Should().BeApproximately(totalMemory, 10); // Allow 10MB tolerance
    }
}
