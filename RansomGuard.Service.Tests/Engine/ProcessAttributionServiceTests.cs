using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using RansomGuard.Core.Services;
using RansomGuard.Service.Engine;
using Xunit;

namespace RansomGuard.Tests.Engine
{
    public class ProcessAttributionServiceTests : IDisposable
    {
        private readonly Mock<IProcessIdentityClassifier> _mockClassifier;
        private readonly ProcessAttributionService _service;

        public ProcessAttributionServiceTests()
        {
            _mockClassifier = new Mock<IProcessIdentityClassifier>();
            _service = new ProcessAttributionService(_mockClassifier.Object);
        }

        // --- Constructor ---

        [Fact]
        public void Constructor_NullClassifier_ShouldThrow()
        {
            Action act = () => new ProcessAttributionService(null!);
            act.Should().Throw<ArgumentNullException>().WithParameterName("processClassifier");
        }

        // --- No identification needed ---

        [Fact]
        public async Task IdentifyProcessAsync_NeedsFalse_ShouldReturnProvidedValues()
        {
            var result = await _service.IdentifyProcessAsync(
                path: "C:\\test\\file.txt",
                action: "CHANGED",
                providedProcessId: 0,
                providedProcessName: "notepad",
                needsIdentification: false);

            result.ProcessName.Should().Be("notepad");
            result.ProcessId.Should().Be(0);
            result.IsTrusted.Should().BeFalse();

            // Classifier should NOT be called
            _mockClassifier.Verify(c => c.GetProcessesUsingFileAsync(It.IsAny<string>()), Times.Never);
        }

        // --- Provided PID path ---

        [Fact]
        public async Task IdentifyProcessAsync_WithValidPid_ShouldCheckIdentity()
        {
            // Use the current process PID — guaranteed to exist
            int currentPid = Process.GetCurrentProcess().Id;
            string currentName = Process.GetCurrentProcess().ProcessName;

            _mockClassifier
                .Setup(c => c.DetermineIdentity(It.IsAny<Process>()))
                .Returns((true, "Trusted"));

            var result = await _service.IdentifyProcessAsync(
                path: "C:\\test\\file.txt",
                action: "CHANGED",
                providedProcessId: currentPid,
                providedProcessName: "Unknown",
                needsIdentification: false);

            result.ProcessId.Should().Be(currentPid);
            result.IsTrusted.Should().BeTrue();
            _mockClassifier.Verify(c => c.DetermineIdentity(It.IsAny<Process>()), Times.Once);
        }

        [Fact]
        public async Task IdentifyProcessAsync_WithInvalidPid_ShouldFallbackGracefully()
        {
            // PID 99999 almost certainly doesn't exist
            var result = await _service.IdentifyProcessAsync(
                path: "C:\\test\\file.txt",
                action: "CHANGED",
                providedProcessId: 99999,
                providedProcessName: "ghost",
                needsIdentification: false);

            // Should not throw; returns whatever was provided
            result.Should().NotBeNull();
        }

        // --- Restart Manager path ---

        [Fact]
        public async Task IdentifyProcessAsync_NeedsIdentification_ShouldCallRestartManager()
        {
            _mockClassifier
                .Setup(c => c.GetProcessesUsingFileAsync(It.IsAny<string>()))
                .ReturnsAsync(new List<Process>());

            var result = await _service.IdentifyProcessAsync(
                path: "C:\\test\\suspicious.crypt",
                action: "CHANGED",
                providedProcessId: 0,
                providedProcessName: "Unknown",
                needsIdentification: true);

            _mockClassifier.Verify(c => c.GetProcessesUsingFileAsync("C:\\test\\suspicious.crypt"), Times.Once);
            result.Should().NotBeNull();
        }

        [Fact]
        public async Task IdentifyProcessAsync_RestartManagerReturnsProcess_ShouldUseIt()
        {
            var fakeProcess = Process.GetCurrentProcess();

            _mockClassifier
                .Setup(c => c.GetProcessesUsingFileAsync(It.IsAny<string>()))
                .ReturnsAsync(new List<Process> { fakeProcess });

            _mockClassifier
                .Setup(c => c.DetermineIdentity(fakeProcess))
                .Returns((true, "Trusted"));

            var result = await _service.IdentifyProcessAsync(
                path: "C:\\test\\file.txt",
                action: "CHANGED",
                providedProcessId: 0,
                providedProcessName: "Unknown",
                needsIdentification: true);

            result.ProcessName.Should().Be(fakeProcess.ProcessName);
            result.IsTrusted.Should().BeTrue();
        }

        [Fact]
        public async Task IdentifyProcessAsync_RestartManagerTimesOut_ShouldFallbackToInference()
        {
            // Simulate a slow Restart Manager call
            _mockClassifier
                .Setup(c => c.GetProcessesUsingFileAsync(It.IsAny<string>()))
                .Returns(async () =>
                {
                    await Task.Delay(10000); // Way longer than the 3s timeout
                    return new List<Process>();
                });

            var result = await _service.IdentifyProcessAsync(
                path: @"C:\Users\test\Documents\file.docx",
                action: "CHANGED",
                providedProcessId: 0,
                providedProcessName: "Unknown",
                needsIdentification: true);

            // Should complete (not hang) and fall back to context inference
            result.Should().NotBeNull();
            result.ProcessName.Should().NotBeNullOrEmpty();
        }

        // --- Context inference ---

        [Theory]
        [InlineData(@"C:\Users\test\Downloads\file.txt", "explorer")]
        [InlineData(@"C:\Users\test\Pictures\Screenshots\shot.png", "SnippingTool")]
        [InlineData(@"C:\Users\test\Desktop\note.txt", "notepad")]
        [InlineData(@"C:\Users\test\Documents\report.docx", "WINWORD")]
        [InlineData(@"C:\Users\test\Documents\budget.xlsx", "EXCEL")]
        [InlineData(@"C:\Users\test\AppData\Local\app.dat", "Application")]
        [InlineData(@"C:\Windows\Temp\tmp123.tmp", "System Process")]
        public async Task IdentifyProcessAsync_ContextInference_ShouldReturnExpectedProcess(string path, string expectedProcess)
        {
            _mockClassifier
                .Setup(c => c.GetProcessesUsingFileAsync(It.IsAny<string>()))
                .ReturnsAsync(new List<Process>());

            var result = await _service.IdentifyProcessAsync(
                path: path,
                action: "CHANGED",
                providedProcessId: 0,
                providedProcessName: "Unknown",
                needsIdentification: true);

            result.ProcessName.Should().Be(expectedProcess);
        }

        public void Dispose()
        {
            _service.Dispose();
        }
    }
}
