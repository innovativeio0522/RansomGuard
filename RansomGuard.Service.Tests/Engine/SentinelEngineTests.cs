using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using RansomGuard.Core.Models;
using RansomGuard.Core.Services;
using RansomGuard.Service.Engine;
using RansomGuard.Service.Services;
using Xunit;

namespace RansomGuard.Tests.Engine
{
    public class SentinelEngineTests
    {
        private readonly Mock<ITelemetryService> _mockTelemetry;
        private readonly Mock<HistoryManager> _mockHistoryManager;
        private readonly Mock<IEntropyAnalyzer> _mockEntropy;
        private readonly Mock<IProcessIdentityClassifier> _mockIdentity;
        private readonly Mock<IQuarantineService> _mockQuarantine;
        private readonly SentinelEngine _engine;

        public SentinelEngineTests()
        {
            _mockTelemetry = new Mock<ITelemetryService>();
            
            // HistoryManager needs a Store mock
            var mockStore = new Mock<IHistoryStore>();
            _mockHistoryManager = new Mock<HistoryManager>(mockStore.Object);
            
            _mockEntropy = new Mock<IEntropyAnalyzer>();
            _mockIdentity = new Mock<IProcessIdentityClassifier>();
            _mockQuarantine = new Mock<IQuarantineService>();

            ConfigurationService.Instance.IsTestingMode = true;

            _engine = new SentinelEngine(
                _mockTelemetry.Object,
                _mockHistoryManager.Object,
                _mockEntropy.Object,
                _mockIdentity.Object,
                _mockQuarantine.Object);
        }

        [Fact]
        public async Task OnFileChanged_ShouldRecordActivity_WhenFileEventOccurs()
        {
            // Arrange
            string testPath = "C:\\test\\file.txt";
            _mockEntropy.Setup(e => e.IsSuspiciousExtension(testPath)).Returns(false);
            _mockEntropy.Setup(e => e.CalculateShannonEntropy(testPath)).Returns(1.5);

            // Act
            _engine.OnFileChanged(testPath, "CHANGED");
            await Task.Delay(200); // Wait for background processor

            // Assert
            _mockHistoryManager.Verify(h => h.AddActivity(It.Is<FileActivity>(a => a.FilePath == testPath)), Times.Once);
        }

        [Fact]
        public async Task OnFileChanged_ShouldReportThreat_WhenEntropyExceedsThreshold()
        {
            // Arrange
            string testPath = "C:\\test\\secret.crypt";
            _mockEntropy.Setup(e => e.IsSuspiciousExtension(testPath)).Returns(true);
            _mockEntropy.Setup(e => e.CalculateShannonEntropy(testPath)).Returns(7.9);
            _mockEntropy.Setup(e => e.IsMediaFile(testPath)).Returns(false);
            _mockHistoryManager.Setup(h => h.ShouldReportThreat(testPath, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>())).Returns(true);

            bool threatRaised = false;
            _engine.ThreatDetected += (t) => { if (t.Path == testPath) threatRaised = true; };

            // Act
            _engine.OnFileChanged(testPath, "CHANGED");
            await Task.Delay(200); // Wait for background processor

            // Assert
            threatRaised.Should().BeTrue();
            _mockHistoryManager.Verify(h => h.AddThreat(It.Is<Threat>(t => t.Path == testPath)), Times.Once);
        }

        [Fact]
        public async Task CheckMassChangeVelocity_ShouldTriggerCriticalThreat_WhenThresholdExceeded()
        {
            // Arrange
            bool criticalThreatRaised = false;
            _engine.ThreatDetected += (t) => { if (t.Severity == ThreatSeverity.Critical) criticalThreatRaised = true; };
            _mockHistoryManager.Setup(h => h.ShouldReportThreat(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>())).Returns(true);

            // Act: Simulate many rapid changes
            for (int i = 0; i < 35; i++)
            {
                _engine.OnFileChanged($"C:\\test\\file{i}.txt", "CHANGED");
            }
            await Task.Delay(2500); // Wait for background processing (35 * 50ms = 1750ms)

            // Assert
            criticalThreatRaised.Should().BeTrue();
        }

        [Fact]
        public async Task HandleMassEncryptionResponse_ShouldQuarantineOnce_WhenThreatIsClaimed()
        {
            // Arrange
            string tempFile = Path.GetTempFileName();
            Threat? claimedThreat = new Threat
            {
                Id = "mass-1",
                Path = "ALL_DRIVES",
                ActionTaken = "Mitigating"
            };

            _mockHistoryManager
                .Setup(h => h.TryBeginMassEncryptionMitigation("mass-1", out claimedThreat))
                .Returns(true);

            // Act
            try
            {
                await _engine.HandleMassEncryptionResponse(
                    "mass-1",
                    shouldMitigate: true,
                    isUserInitiated: true,
                    processId: 0,
                    processName: "testproc",
                    filesToQuarantine: new List<string> { tempFile });

                // Assert
                _mockQuarantine.Verify(q => q.QuarantineFile(tempFile), Times.Once);
            }
            finally
            {
                if (File.Exists(tempFile))
                {
                    File.Delete(tempFile);
                }
            }
        }

        [Fact]
        public async Task HandleMassEncryptionResponse_ShouldSkipMitigation_WhenThreatWasAlreadyResolved()
        {
            // Arrange
            Threat? unclaimedThreat = null;
            _mockHistoryManager
                .Setup(h => h.TryBeginMassEncryptionMitigation("mass-2", out unclaimedThreat))
                .Returns(false);

            // Act
            await _engine.HandleMassEncryptionResponse(
                "mass-2",
                shouldMitigate: true,
                isUserInitiated: true,
                processId: 0,
                processName: "testproc",
                filesToQuarantine: new List<string> { "C:\\test\\doc2.txt" });

            // Assert
            _mockQuarantine.Verify(q => q.QuarantineFile(It.IsAny<string>()), Times.Never);
        }

        [Fact]
        public async Task HandleMassEncryptionResponse_ShouldRecordDecline_WithoutMitigation()
        {
            // Arrange
            Threat? declinedThreat = new Threat
            {
                Id = "mass-3",
                Path = "ALL_DRIVES",
                ActionTaken = "User Declined"
            };

            _mockHistoryManager
                .Setup(h => h.TryDeclineMassEncryptionThreat("mass-3", out declinedThreat))
                .Returns(true);

            // Act
            await _engine.HandleMassEncryptionResponse(
                "mass-3",
                shouldMitigate: false,
                isUserInitiated: true,
                processId: 0,
                processName: "testproc",
                filesToQuarantine: new List<string> { "C:\\test\\doc3.txt" });

            // Assert
            _mockQuarantine.Verify(q => q.QuarantineFile(It.IsAny<string>()), Times.Never);
        }
    }
}
