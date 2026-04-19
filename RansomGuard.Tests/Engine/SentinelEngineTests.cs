using System;
using System.IO;
using System.Linq;
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
        private readonly Mock<IEntropyAnalyzer> _mockEntropy;
        private readonly Mock<IProcessIdentityClassifier> _mockIdentity;
        private readonly Mock<IHistoryStore> _mockHistory;
        private readonly Mock<IQuarantineService> _mockQuarantine;
        private readonly SentinelEngine _engine;

        public SentinelEngineTests()
        {
            _mockEntropy = new Mock<IEntropyAnalyzer>();
            _mockIdentity = new Mock<IProcessIdentityClassifier>();
            _mockHistory = new Mock<IHistoryStore>();
            _mockQuarantine = new Mock<IQuarantineService>();

            // Prevent tests from over-writing real production config
            ConfigurationService.Instance.IsTestingMode = true;

            _engine = new SentinelEngine(
                _mockEntropy.Object,
                _mockIdentity.Object,
                _mockHistory.Object,
                _mockQuarantine.Object);
        }

        [Fact]
        public void OnFileChanged_ShouldRecordActivity_WhenFileEventOccurs()
        {
            // Arrange
            string testPath = "C:\\test\\file.txt";
            _mockEntropy.Setup(e => e.IsSuspiciousExtension(testPath)).Returns(false);
            _mockEntropy.Setup(e => e.CalculateShannonEntropy(testPath)).Returns(1.5);

            // Act
            _engine.OnFileChanged(testPath, "CHANGED");

            // Assert
            _mockHistory.Verify(h => h.SaveActivityAsync(It.Is<FileActivity>(a => a.FilePath == testPath)), Times.Once);
        }

        [Fact]
        public void OnFileChanged_ShouldReportThreat_WhenEntropyExceedsThreshold()
        {
            // Arrange
            string testPath = "C:\\test\\secret.crypt";
            _mockEntropy.Setup(e => e.IsSuspiciousExtension(testPath)).Returns(true);
            _mockEntropy.Setup(e => e.CalculateShannonEntropy(testPath)).Returns(7.9);
            _mockEntropy.Setup(e => e.IsMediaFile(testPath)).Returns(false);

            bool threatRaised = false;
            _engine.ThreatDetected += (t) => { if (t.Path == testPath) threatRaised = true; };

            // Act
            _engine.OnFileChanged(testPath, "CHANGED");

            // Assert
            threatRaised.Should().BeTrue();
            _mockHistory.Verify(h => h.SaveThreatAsync(It.Is<Threat>(t => t.Path == testPath)), Times.Once);
        }

        [Fact]
        public void CheckMassChangeVelocity_ShouldTriggerCriticalThreat_WhenThresholdExceeded()
        {
            // Arrange
            bool criticalThreatRaised = false;
            _engine.ThreatDetected += (t) => { if (t.Severity == ThreatSeverity.Critical) criticalThreatRaised = true; };

            // Act: Simulate many rapid changes (Threshold for High Sensitivity is 30)
            for (int i = 0; i < 35; i++)
            {
                _engine.OnFileChanged($"C:\\test\\file{i}.txt", "CHANGED");
            }

            // Assert
            criticalThreatRaised.Should().BeTrue();
        }

        [Fact]
        public async Task PerformQuickScan_ShouldDetectHiddenEncryption_EvenWithNormalExtension()
        {
            // Arrange
            string testDir = Path.Combine(Path.GetTempPath(), "RG_ScanTest_" + Guid.NewGuid());
            Directory.CreateDirectory(testDir);
            string normalFile = Path.Combine(testDir, "normal_but_encrypted.txt");
            File.WriteAllText(normalFile, "Some bootstrap data to ensure file exists.");

            ConfigurationService.Instance.MonitoredPaths.Clear();
            ConfigurationService.Instance.MonitoredPaths.Add(testDir);

            _mockEntropy.Setup(e => e.IsSuspiciousExtension(normalFile)).Returns(false);
            _mockEntropy.Setup(e => e.CalculateShannonEntropy(normalFile)).Returns(7.9); // High entropy
            _mockEntropy.Setup(e => e.IsMediaFile(normalFile)).Returns(false);

            int threatsDetected = 0;
            _engine.ThreatDetected += (t) => threatsDetected++;

            try
            {
                // Act
                await _engine.PerformQuickScan();

                // Assert
                threatsDetected.Should().Be(1);
                _mockHistory.Verify(h => h.SaveThreatAsync(It.Is<Threat>(t => t.Path == normalFile && t.Name.Contains("Hidden Encryption"))), Times.Once);
            }
            finally
            {
                if (Directory.Exists(testDir)) Directory.Delete(testDir, true);
            }
        }
    }
}
