using RansomGuard.Core.Models;
using FluentAssertions;
using Xunit;

namespace RansomGuard.Tests.Models;

public class FileActivityTests
{
    [Fact]
    public void FileActivity_ShouldInitializeWithDefaultValues()
    {
        // Arrange & Act
        var activity = new FileActivity();

        // Assert
        activity.FilePath.Should().BeEmpty();
        activity.Action.Should().Be("READ"); // Default value
        activity.ProcessName.Should().Be("System"); // Default value
        activity.IsSuspicious.Should().BeFalse();
        activity.Timestamp.Should().BeCloseTo(DateTime.Now, TimeSpan.FromSeconds(1));
    }

    [Fact]
    public void FileActivity_ShouldAllowSettingProperties()
    {
        // Arrange
        var activity = new FileActivity
        {
            FilePath = "C:\\test\\document.docx",
            Action = "MODIFIED",
            ProcessName = "winword.exe",
            IsSuspicious = false,
            Timestamp = DateTime.Now
        };

        // Assert
        activity.FilePath.Should().Be("C:\\test\\document.docx");
        activity.Action.Should().Be("MODIFIED");
        activity.ProcessName.Should().Be("winword.exe");
        activity.IsSuspicious.Should().BeFalse();
    }

    [Theory]
    [InlineData("CREATED")]
    [InlineData("MODIFIED")]
    [InlineData("DELETED")]
    [InlineData("RENAMED")]
    public void FileActivity_ShouldAcceptCommonActions(string action)
    {
        // Arrange & Act
        var activity = new FileActivity { Action = action };

        // Assert
        activity.Action.Should().Be(action);
    }

    [Fact]
    public void FileActivity_IsSuspicious_ShouldBeToggleable()
    {
        // Arrange
        var activity = new FileActivity { IsSuspicious = false };

        // Act
        activity.IsSuspicious = true;

        // Assert
        activity.IsSuspicious.Should().BeTrue();
    }
}
