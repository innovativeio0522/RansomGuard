using System;
using System.IO;
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
            byte[] data = Encoding.UTF8.GetBytes(new string('A', 4096));
            string tempFile = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".txt");
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
            // Arrange: 4KB of random bytes
            byte[] data = new byte[4096];
            new Random(42).NextBytes(data); 
            string tempFile = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".dat");
            File.WriteAllBytes(tempFile, data);

            try
            {
                // Act
                double entropy = _service.CalculateShannonEntropy(tempFile);

                // Assert: Random data has high entropy
                entropy.Should().BeGreaterThan(7.5);
            }
            finally
            {
                if (File.Exists(tempFile)) File.Delete(tempFile);
            }
        }

        [Fact]
        public void CalculateShannonEntropy_MultiPointSampling_ShouldDetectHiddenEncryptionInMiddle()
        {
            // Arrange: Create a 100KB file
            // [ 0-32KB: Zeros ] [ 32-36KB: Random ] [ 36-100KB: Zeros ]
            int totalSize = 102400;
            byte[] data = new byte[totalSize];
            
            // Inject 4KB of random data into the middle (approx 50KB mark)
            byte[] randomChunk = new byte[4096];
            new Random(123).NextBytes(randomChunk);
            Array.Copy(randomChunk, 0, data, 51200, 4096);

            string tempFile = Path.Combine(Path.GetTempPath(), "MultiPointTest_" + Guid.NewGuid() + ".bin");
            File.WriteAllBytes(tempFile, data);

            try
            {
                // Act
                double entropy = _service.CalculateShannonEntropy(tempFile);

                // Assert: Even though the head and tail are zero entropy, 
                // multi-point sampling should find the middle chunk.
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
        public void IsSuspiciousExtension_ShouldCorrectlyIdentifyHeuristicMatches(string filename, bool expected)
        {
            bool result = _service.IsSuspiciousExtension(filename);
            result.Should().Be(expected);
        }

        [Theory]
        [InlineData("test.jpg", true)]
        [InlineData("test.mp4", true)]
        [InlineData("test.zip", true)]
        public void IsMediaFile_ShouldCorrectlyIdentifyMediaAndArchives(string filename, bool expected)
        {
            bool result = _service.IsMediaFile(filename);
            result.Should().Be(expected);
        }

        [Theory]
        [InlineData("app.exe", true)]
        [InlineData("data.dll", true)]
        [InlineData("debug.pdb", true)]
        [InlineData("test.txt", false)]
        public void IsHighEntropyExtension_ShouldReturnTrueForBinaries(string filename, bool expected)
        {
            bool result = _service.IsHighEntropyExtension(filename);
            result.Should().Be(expected);
        }

        [Theory]
        [InlineData("C:\\Project\\obj", true)]
        [InlineData("C:\\Project\\bin", true)]
        [InlineData("C:\\Project\\.git", true)]
        [InlineData("C:\\Project\\src", false)]
        public void ShouldSkipDirectory_ShouldReturnTrueForExcludedFolders(string path, bool expected)
        {
            bool result = _service.ShouldSkipDirectory(path);
            result.Should().Be(expected);
        }
    }
}
