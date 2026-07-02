using System;
using System.Threading.Tasks;
using FluentAssertions;
using RansomGuard.Service.Engine;
using Xunit;

namespace RansomGuard.Tests.Engine
{
    /// <summary>
    /// Tests for CriticalResponseService.
    /// Note: ExecuteCriticalResponse and CheckVssIntegrityAsync invoke real OS commands
    /// (powershell, vssadmin, shutdown). These tests verify the service doesn't throw
    /// and handles errors gracefully — they do NOT actually isolate the network or shutdown.
    /// </summary>
    public class CriticalResponseServiceTests
    {
        private readonly CriticalResponseService _service;

        public CriticalResponseServiceTests()
        {
            _service = new CriticalResponseService();
        }

        // --- ExecuteCriticalResponse: both disabled ---

        [Fact]
        public void ExecuteCriticalResponse_BothDisabled_ShouldNotThrow()
        {
            Action act = () => _service.ExecuteCriticalResponse(
                networkIsolationEnabled: false,
                emergencyShutdownEnabled: false);

            act.Should().NotThrow();
        }

        // --- ExecuteCriticalResponse: network isolation only ---

        [Fact]
        public void ExecuteCriticalResponse_NetworkIsolationEnabled_ShouldNotThrow()
        {
            // This starts a powershell process but won't actually disable adapters
            // in a test environment (no elevated privileges). It should still not throw.
            Action act = () => _service.ExecuteCriticalResponse(
                networkIsolationEnabled: true,
                emergencyShutdownEnabled: false);

            act.Should().NotThrow();
        }

        // --- CheckVssIntegrityAsync ---

        [Fact]
        public async Task CheckVssIntegrityAsync_ShouldCompleteWithoutThrowing()
        {
            // vssadmin may not be available in all test environments, but the method
            // should handle that gracefully via its internal try/catch.
            Func<Task> act = async () => await _service.CheckVssIntegrityAsync();

            await act.Should().NotThrowAsync();
        }

        [Fact]
        public async Task CheckVssIntegrityAsync_ShouldCompleteInReasonableTime()
        {
            var start = DateTime.Now;
            await _service.CheckVssIntegrityAsync();
            var elapsed = DateTime.Now - start;

            // Should complete within 10 seconds even if vssadmin is slow
            elapsed.TotalSeconds.Should().BeLessThan(10);
        }
    }
}
