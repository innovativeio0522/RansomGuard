using RansomGuard.Core.IPC;
using FluentAssertions;
using Xunit;

namespace RansomGuard.Tests.IPC;

public class IpcModelsTests
{
    [Fact]
    public void IpcPacket_ShouldHaveCurrentVersion()
    {
        // Arrange & Act
        var packet = new IpcPacket();

        // Assert
        packet.Version.Should().Be(IpcPacket.CurrentVersion);
    }

    [Fact]
    public void IpcPacket_CurrentVersion_ShouldBeOne()
    {
        // Arrange & Act
        var version = IpcPacket.CurrentVersion;

        // Assert
        version.Should().Be(1);
    }

    [Fact]
    public void IpcPacket_ShouldAllowSettingProperties()
    {
        // Arrange
        var packet = new IpcPacket
        {
            Type = MessageType.FileActivity,
            Payload = "{\"test\":\"data\"}"
        };

        // Assert
        packet.Type.Should().Be(MessageType.FileActivity);
        packet.Payload.Should().Be("{\"test\":\"data\"}");
    }

    [Theory]
    [InlineData(MessageType.FileActivity)]
    [InlineData(MessageType.ThreatDetected)]
    [InlineData(MessageType.TelemetryUpdate)]
    [InlineData(MessageType.CommandRequest)]
    public void IpcPacket_ShouldAcceptAllMessageTypes(MessageType messageType)
    {
        // Arrange & Act
        var packet = new IpcPacket { Type = messageType };

        // Assert
        packet.Type.Should().Be(messageType);
    }

    [Fact]
    public void TelemetryData_ShouldInitializeWithDefaultValues()
    {
        // Arrange & Act
        var telemetry = new TelemetryData();

        // Assert
        telemetry.CpuUsage.Should().Be(0);
        telemetry.MemoryUsage.Should().Be(0);
        telemetry.MonitoredFilesCount.Should().Be(0);
        telemetry.ProcessesCount.Should().Be(0);
    }

    [Fact]
    public void CommandRequest_ShouldAllowSettingProperties()
    {
        // Arrange
        var command = new CommandRequest
        {
            Command = CommandType.KillProcess,
            Arguments = "1234"
        };

        // Assert
        command.Command.Should().Be(CommandType.KillProcess);
        command.Arguments.Should().Be("1234");
    }
}
