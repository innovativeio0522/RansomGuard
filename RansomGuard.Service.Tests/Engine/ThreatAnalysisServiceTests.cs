using System;
using System.IO;
using FluentAssertions;
using Moq;
using RansomGuard.Service.Engine;
using RansomGuard.Core.Services;
using Xunit;

namespace RansomGuard.Tests.Engine
{
    public class ThreatAnalysisServiceTests
    {
        private readonly Mock<IEntropyAnalyzer> _mockEntropy;
        private readonly ThreatAnalysisService _service;

        public ThreatAnalysisServiceTests()
        {
            _mockEntropy = new Mock<IEntropyAnalyzer>();
            _service = new ThreatAnalysisService(_mockEntropy.Object);
        }

        // --- Constructor ---

        [Fact]
        public void Constructor_NullEntropyAnalyzer_ShouldThrow()
        {
            Action act = () => new ThreatAnalysisService(null!);
            act.Should().Throw<ArgumentNullException>().WithParameterName("entropyAnalyzer");
        }

        // --- ShouldSkip ---

        [Fact]
        public void AnalyzeFile_ExcludedDirectory_ShouldSkip()
        {
            _mockEntropy.Setup(e => e.ShouldSkipDirectory(It.IsAny<string>())).Returns(true);

            var result = _service.AnalyzeFile(@"C:\obj\file.txt", "CHANGED", false, 3);

            result.ShouldSkip.Should().BeTrue();
            result.SkipReason.Should().Be("Excluded directory");
        }

        [Fact]
        public void AnalyzeFile_IsDirectory_ShouldSkip()
        {
            _mockEntropy.Setup(e => e.ShouldSkipDirectory(It.IsAny<string>())).Returns(false);

            // Use a path that actually exists as a directory
            string tempDir = Path.Combine(Path.GetTempPath(), "RG_DirTest_" + Guid.NewGuid());
            Directory.CreateDirectory(tempDir);

            try
            {
                var result = _service.AnalyzeFile(tempDir, "CHANGED", false, 3);
                result.ShouldSkip.Should().BeTrue();
                result.SkipReason.Should().Be("Is directory");
            }
            finally
            {
                Directory.Delete(tempDir);
            }
        }

        // --- Suspicious Extension ---

        [Fact]
        public void AnalyzeFile_SuspiciousExtension_ShouldFlagAsSuspicious()
        {
            _mockEntropy.Setup(e => e.ShouldSkipDirectory(It.IsAny<string>())).Returns(false);
            _mockEntropy.Setup(e => e.IsSuspiciousExtension("C:\\test\\file.crypt")).Returns(true);
            _mockEntropy.Setup(e => e.IsMediaFile(It.IsAny<string>())).Returns(false);
            _mockEntropy.Setup(e => e.IsHighEntropyExtension(It.IsAny<string>())).Returns(false);
            _mockEntropy.Setup(e => e.CalculateShannonEntropy(It.IsAny<string>())).Returns(7.9);
            _mockEntropy.Setup(e => e.IsSuspiciousRenamePattern(It.IsAny<string>())).Returns(false);
            _mockEntropy.Setup(e => e.IsExtensionMismatch(It.IsAny<string>())).Returns(false);

            var result = _service.AnalyzeFile("C:\\test\\file.crypt", "CHANGED", false, 3);

            result.HasSuspiciousExtension.Should().BeTrue();
            result.IsSuspicious.Should().BeTrue();
            result.ThreatReason.Should().Be("Suspicious Extension");
        }

        // --- Entropy Threshold by Sensitivity ---

        [Theory]
        [InlineData(1, 7.8)] // Low
        [InlineData(2, 7.5)] // Medium
        [InlineData(3, 7.2)] // High
        [InlineData(4, 6.8)] // Paranoid
        public void AnalyzeFile_EntropyThreshold_VariesBySensitivityLevel(int sensitivity, double expectedThreshold)
        {
            _mockEntropy.Setup(e => e.ShouldSkipDirectory(It.IsAny<string>())).Returns(false);
            _mockEntropy.Setup(e => e.IsSuspiciousExtension(It.IsAny<string>())).Returns(false);
            _mockEntropy.Setup(e => e.IsMediaFile(It.IsAny<string>())).Returns(false);
            _mockEntropy.Setup(e => e.IsHighEntropyExtension(It.IsAny<string>())).Returns(false);
            _mockEntropy.Setup(e => e.CalculateShannonEntropy(It.IsAny<string>())).Returns(expectedThreshold + 0.1);
            _mockEntropy.Setup(e => e.IsSuspiciousRenamePattern(It.IsAny<string>())).Returns(false);
            _mockEntropy.Setup(e => e.IsExtensionMismatch(It.IsAny<string>())).Returns(false);

            var result = _service.AnalyzeFile("C:\\test\\file.txt", "CHANGED", false, sensitivity);

            result.EntropyThreshold.Should().BeApproximately(expectedThreshold, 0.01);
            result.IsHighEntropy.Should().BeTrue();
        }

        // --- Trusted Process raises threshold for media/binary ---

        [Fact]
        public void AnalyzeFile_TrustedProcess_MediaFile_ShouldUseHighThreshold()
        {
            _mockEntropy.Setup(e => e.ShouldSkipDirectory(It.IsAny<string>())).Returns(false);
            _mockEntropy.Setup(e => e.IsSuspiciousExtension(It.IsAny<string>())).Returns(false);
            _mockEntropy.Setup(e => e.IsMediaFile(It.IsAny<string>())).Returns(true);
            _mockEntropy.Setup(e => e.IsHighEntropyExtension(It.IsAny<string>())).Returns(false);
            _mockEntropy.Setup(e => e.CalculateShannonEntropy(It.IsAny<string>())).Returns(7.95);
            _mockEntropy.Setup(e => e.IsSuspiciousRenamePattern(It.IsAny<string>())).Returns(false);
            _mockEntropy.Setup(e => e.IsExtensionMismatch(It.IsAny<string>())).Returns(false);

            var result = _service.AnalyzeFile("C:\\test\\photo.jpg", "CHANGED", isTrustedProcess: true, 3);

            // Trusted process + media = threshold 7.99, so 7.95 should NOT trigger
            result.EntropyThreshold.Should().BeApproximately(7.99, 0.01);
            result.IsHighEntropy.Should().BeFalse();
            result.IsSuspicious.Should().BeFalse();
        }

        // --- Rename pattern ---

        [Fact]
        public void AnalyzeFile_SuspiciousRenamePattern_ShouldFlagAsSuspicious()
        {
            _mockEntropy.Setup(e => e.ShouldSkipDirectory(It.IsAny<string>())).Returns(false);
            _mockEntropy.Setup(e => e.IsSuspiciousExtension(It.IsAny<string>())).Returns(false);
            _mockEntropy.Setup(e => e.IsMediaFile(It.IsAny<string>())).Returns(false);
            _mockEntropy.Setup(e => e.IsHighEntropyExtension(It.IsAny<string>())).Returns(false);
            _mockEntropy.Setup(e => e.CalculateShannonEntropy(It.IsAny<string>())).Returns(1.0);
            _mockEntropy.Setup(e => e.IsSuspiciousRenamePattern(It.IsAny<string>())).Returns(true);
            _mockEntropy.Setup(e => e.IsExtensionMismatch(It.IsAny<string>())).Returns(false);

            var result = _service.AnalyzeFile("C:\\test\\doc.txt", "RENAMED FROM doc.txt TO doc.txt.locked", false, 3);

            result.HasSuspiciousRenamePattern.Should().BeTrue();
            result.IsSuspicious.Should().BeTrue();
            result.ThreatReason.Should().Be("Suspicious Pattern");
        }

        // --- Extension mismatch ---

        [Fact]
        public void AnalyzeFile_ExtensionMismatch_ShouldFlagAsSuspicious()
        {
            _mockEntropy.Setup(e => e.ShouldSkipDirectory(It.IsAny<string>())).Returns(false);
            _mockEntropy.Setup(e => e.IsSuspiciousExtension(It.IsAny<string>())).Returns(false);
            _mockEntropy.Setup(e => e.IsMediaFile(It.IsAny<string>())).Returns(false);
            _mockEntropy.Setup(e => e.IsHighEntropyExtension(It.IsAny<string>())).Returns(false);
            _mockEntropy.Setup(e => e.CalculateShannonEntropy(It.IsAny<string>())).Returns(1.0);
            _mockEntropy.Setup(e => e.IsSuspiciousRenamePattern(It.IsAny<string>())).Returns(false);
            _mockEntropy.Setup(e => e.IsExtensionMismatch(It.IsAny<string>())).Returns(true);

            var result = _service.AnalyzeFile("C:\\test\\image.jpg", "CHANGED", false, 3);

            result.HasExtensionMismatch.Should().BeTrue();
            result.IsSuspicious.Should().BeTrue();
            result.ThreatReason.Should().Be("File Type Mismatch");
        }

        // --- Clean file ---

        [Fact]
        public void AnalyzeFile_CleanFile_ShouldNotBeSuspicious()
        {
            _mockEntropy.Setup(e => e.ShouldSkipDirectory(It.IsAny<string>())).Returns(false);
            _mockEntropy.Setup(e => e.IsSuspiciousExtension(It.IsAny<string>())).Returns(false);
            _mockEntropy.Setup(e => e.IsMediaFile(It.IsAny<string>())).Returns(false);
            _mockEntropy.Setup(e => e.IsHighEntropyExtension(It.IsAny<string>())).Returns(false);
            _mockEntropy.Setup(e => e.CalculateShannonEntropy(It.IsAny<string>())).Returns(2.5);
            _mockEntropy.Setup(e => e.IsSuspiciousRenamePattern(It.IsAny<string>())).Returns(false);
            _mockEntropy.Setup(e => e.IsExtensionMismatch(It.IsAny<string>())).Returns(false);

            var result = _service.AnalyzeFile("C:\\test\\readme.txt", "CHANGED", false, 3);

            result.ShouldSkip.Should().BeFalse();
            result.IsSuspicious.Should().BeFalse();
            result.ThreatReason.Should().BeEmpty();
        }

        // --- LastEntropyScore tracking ---

        [Fact]
        public void LastEntropyScore_ShouldUpdateAfterAnalysis()
        {
            _mockEntropy.Setup(e => e.ShouldSkipDirectory(It.IsAny<string>())).Returns(false);
            _mockEntropy.Setup(e => e.IsSuspiciousExtension(It.IsAny<string>())).Returns(false);
            _mockEntropy.Setup(e => e.IsMediaFile(It.IsAny<string>())).Returns(false);
            _mockEntropy.Setup(e => e.IsHighEntropyExtension(It.IsAny<string>())).Returns(false);
            _mockEntropy.Setup(e => e.CalculateShannonEntropy(It.IsAny<string>())).Returns(6.42);
            _mockEntropy.Setup(e => e.IsSuspiciousRenamePattern(It.IsAny<string>())).Returns(false);
            _mockEntropy.Setup(e => e.IsExtensionMismatch(It.IsAny<string>())).Returns(false);

            _service.AnalyzeFile("C:\\test\\file.txt", "CHANGED", false, 3);

            _service.LastEntropyScore.Should().BeApproximately(6.42, 0.001);
        }
    }
}
