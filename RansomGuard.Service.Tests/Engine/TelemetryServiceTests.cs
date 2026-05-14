using System;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using RansomGuard.Service.Engine;
using Xunit;

namespace RansomGuard.Tests.Engine
{
    public class TelemetryServiceTests : IDisposable
    {
        private readonly TelemetryService _service;

        public TelemetryServiceTests()
        {
            _service = new TelemetryService();
        }

        [Fact]
        public async Task Start_ShouldTriggerInitialTelemetryUpdate()
        {
            // Arrange
            bool eventRaised = false;
            _service.TelemetryUpdated += (data) => eventRaised = true;

            // Act
            _service.Start();
            
            // Wait for first tick (or force manual tick if possible, but it's a private Timer)
            // Since it's a 2s timer, we'll wait a bit.
            await Task.Delay(2500);

            // Assert
            eventRaised.Should().BeTrue();
            var telemetry = _service.GetLatestTelemetry();
            telemetry.ProcessesCount.Should().BeGreaterThan(0);
            telemetry.ActiveThreadsCount.Should().BeGreaterThan(0);
        }

        [Fact]
        public void GetLatestTelemetry_ShouldReturnValidDataAfterTick()
        {
            // Act
            _service.Start();
            // Wait for sample 
            Thread.Sleep(2500); 

            // Assert
            var data = _service.GetLatestTelemetry();
            data.CpuUsage.Should().BeInRange(0, 100);
            data.SystemRamTotalMb.Should().BeGreaterThan(0);
            data.TrustedProcessPercent.Should().BeInRange(50, 100);
        }

        public void Dispose()
        {
            _service.Stop();
            _service.Dispose();
        }
    }
}
