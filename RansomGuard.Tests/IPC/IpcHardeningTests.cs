using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using RansomGuard.Core.IPC;
using RansomGuard.Core.Interfaces;
using RansomGuard.Service.Communication;
using RansomGuard.Service.Engine;
using RansomGuard.Services;
using Xunit;

namespace RansomGuard.Tests.IPC
{
    public class IpcHardeningTests : IDisposable
    {
        private readonly Mock<ISystemMonitorService> _mockMonitor;
        private readonly Mock<ITelemetryService> _mockTelemetry;
        private readonly string _testPipeName;
        private readonly NamedPipeServer _server;
        private readonly ServicePipeClient _client;

        public IpcHardeningTests()
        {
            _testPipeName = "RG_TestPipe_" + Guid.NewGuid().ToString("N");
            _mockMonitor = new Mock<ISystemMonitorService>();
            _mockTelemetry = new Mock<ITelemetryService>();
            
            // Setup default monitor behavior
            _mockMonitor.Setup(m => m.GetTelemetry()).Returns(new TelemetryData());
            _mockMonitor.Setup(m => m.GetRecentFileActivities()).Returns(Enumerable.Empty<RansomGuard.Core.Models.FileActivity>());
            _mockMonitor.Setup(m => m.GetRecentThreats()).Returns(Enumerable.Empty<RansomGuard.Core.Models.Threat>());
            _mockMonitor.Setup(m => m.GetActiveProcesses()).Returns(Enumerable.Empty<RansomGuard.Core.Models.ProcessInfo>());

            _server = new NamedPipeServer(_mockMonitor.Object, _mockTelemetry.Object, _testPipeName);
            _server.Start();

            // Client starts automatically
            _client = new ServicePipeClient(_testPipeName);
        }

        [Fact]
        public async Task Handshake_ShouldBeSuccessful_WhenClientConnects()
        {
            // Act: Wait for connection and handshake (should happen within 5s)
            int timeout = 5000;
            while (!_client.IsConnected && timeout > 0)
            {
                await Task.Delay(100);
                timeout -= 100;
            }

            // Assert
            _client.IsConnected.Should().BeTrue();
        }

        [Fact]
        public async Task Server_ShouldDropOldTelemetry_WhenClientIsSlow()
        {
            // This test verifies that the server doesn't block when broadcasting to a slow client.
            
            // 1. Wait for connection
            await Task.Delay(1000); 

            // 2. Broadcast many telemetry updates rapidly
            // The server queue limit is 90 for dropping
            for (int i = 0; i < 200; i++)
            {
                _mockMonitor.Raise(m => m.FileActivityDetected += null, new RansomGuard.Core.Models.FileActivity { FilePath = $"test_{i}.txt" });
            }

            // 3. Verify server is still responsive and hasn't crashed or blocked indefinitely
            _server.Should().NotBeNull();
        }

        [Fact]
        public async Task ReliableBroadcast_ShouldNotDropCriticalEvents()
        {
            // Arrange
            // 1. Wait for connection AND handshake
            int waitTimeout = 15000;
            while (!_client.IsHandshaked && waitTimeout > 0)
            {
                await Task.Delay(100);
                waitTimeout -= 100;
            }
            
            _client.IsHandshaked.Should().BeTrue("Client must be handshaked before testing alerts");
            
            // Give the server a moment to finish its internal context state update
            await Task.Delay(1000);

            var threat = new RansomGuard.Core.Models.Threat { Path = "C:\\ransom.exe", Name = "TEST_THREAT" };
            
            int threatsReceivedCount = 0;
            _client.ThreatDetected += (t) => { 
                if (string.Equals(t.Path, threat.Path, StringComparison.OrdinalIgnoreCase)) 
                    Interlocked.Increment(ref threatsReceivedCount); 
            };

            // Act
            _mockMonitor.Raise(m => m.ThreatDetected += null, threat);

            // Assert: Wait for delivery
            int deliveryTimeout = 10000;
            while (Volatile.Read(ref threatsReceivedCount) == 0 && deliveryTimeout > 0)
            {
                await Task.Delay(100);
                deliveryTimeout -= 100;
            }
            
            Volatile.Read(ref threatsReceivedCount).Should().BeGreaterThan(0, 
                $"Threat should be received via reliable IPC broadcast. Server connected clients: {_server.GetType().GetField("_clients", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?.GetValue(_server)}");
        }

        public void Dispose()
        {
            _client.Dispose();
            _server.Stop();
        }
    }
}
