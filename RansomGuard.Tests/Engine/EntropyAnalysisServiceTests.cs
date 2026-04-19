using System.Text;
using FluentAssertions;
using RansomGuard.Service.Engine;
using Xunit;

namespace RansomGuard.Tests.Engine
{
    public class EntropyAnalysisServiceTests
    {
        private readonly EntropyAnalysisService _service;

        public EntropyAnalysisServiceTests()
        {
            _service = new EntropyAnalysisService();
        }

        [Fact]
        public void CalculateShannonEntropy_LowEntropy_ShouldBeNearZero()
        {
            // Arrange: 1KB of identical characters
            byte[] data = Encoding.UTF8.GetBytes(new string('A', 1024));
            string tempFile = Path.GetTempFileName();
            File.WriteAllBytes(tempFile, data);

            try
            {
                // Act
                double entropy = _service.CalculateShannonEntropy(tempFile);

                // Assert: Entropy of identical data is 0
                entropy.Should().BeInRange(0, 0.1);
            }
            finally
            {
                if (File.Exists(tempFile)) File.Delete(tempFile);
            }
        }

        [Fact]
        public void CalculateShannonEntropy_HighEntropy_ShouldBeNearEight()
        {
            // Arrange: 1KB of random bytes
            byte[] data = new byte[1024];
            new Random(42).NextBytes(data); // Seeded for reproducibility
            string tempFile = Path.GetTempFileName();
            File.WriteAllBytes(tempFile, data);

            try
            {
                // Act
                double entropy = _service.CalculateShannonEntropy(tempFile);

                // Assert: Random data has high entropy (approaching 8 bits per byte)
                entropy.Should().BeGreaterThan(7.0);
            }
            finally
            {
                if (File.Exists(tempFile)) File.Delete(tempFile);
            }
        }

        [Theory]
        [InlineData("test.crypt", true)]
        [InlineData("test.locked", true)]
        [InlineData("test.txt.enc", true)]
        [InlineData("test.txt", false)]
        [InlineData("test.pdf", false)]
        public void IsSuspiciousExtension_ShouldCorrectlyIdentifyHeuristicMatches(string filename, bool expected)
        {
            // Act
            bool result = _service.IsSuspiciousExtension(filename);

            // Assert
            result.Should().Be(expected);
        }

        [Theory]
        [InlineData("test.jpg", true)]
        [InlineData("test.mp4", true)]
        [InlineData("test.zip", true)]
        [InlineData("test.txt", false)]
        public void IsMediaFile_ShouldCorrectlyIdentifyMediaAndArchives(string filename, bool expected)
        {
            // Act
            bool result = _service.IsMediaFile(filename);

            // Assert
            result.Should().Be(expected);
        }

        [Theory]
        [InlineData("RENAMED FROM old.txt TO new.crypt", true)]
        [InlineData("RENAMED FROM doc.pdf TO README.locked", true)]
        [InlineData("CREATED", false)]
        [InlineData("CHANGED", false)]
        public void IsSuspiciousRenamePattern_ShouldDetectRansomwareNamingBehaviors(string action, bool expected)
        {
            // Act
            bool result = _service.IsSuspiciousRenamePattern(action);

            // Assert
            result.Should().Be(expected);
        }
    }
}
