using System.IO;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using RansomGuard.Service.Engine;
using RansomGuard.Service.Services;
using Xunit;

namespace RansomGuard.Tests.Engine
{
    public class QuarantineServiceTests : IDisposable
    {
        private readonly Mock<IHistoryStore> _mockHistory;
        private readonly string _testQuarantinePath;
        private readonly string _tempSourcePath;
        private readonly QuarantineService _service;

        public QuarantineServiceTests()
        {
            _mockHistory = new Mock<IHistoryStore>();
            _testQuarantinePath = Path.Combine(Path.GetTempPath(), "RG_Test_Quarantine_" + Guid.NewGuid());
            
            // Use user profile path for test files (required by security validation)
            string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            _tempSourcePath = Path.Combine(userProfile, "RG_Test_Source_" + Guid.NewGuid());
            
            Directory.CreateDirectory(_testQuarantinePath);
            Directory.CreateDirectory(_tempSourcePath);
            
            _service = new QuarantineService(_mockHistory.Object, _testQuarantinePath);
        }

        [Fact]
        public async Task QuarantineFile_ShouldMoveFileToQuarantineAndSaveMetadata()
        {
            // Arrange
            string sourceFile = Path.Combine(_tempSourcePath, "test_threat.txt");
            File.WriteAllText(sourceFile, "This is a fake ransomware file.");
            
            // Act
            await _service.QuarantineFile(sourceFile);

            // Assert
            File.Exists(sourceFile).Should().BeFalse();
            
            string[] quarantinedFiles = Directory.GetFiles(_testQuarantinePath, "*.quarantine");
            quarantinedFiles.Should().HaveCount(1);
            
            string metaFile = quarantinedFiles[0] + ".metadata";
            File.Exists(metaFile).Should().BeTrue();
            
            string metaContent = File.ReadAllText(metaFile);
            metaContent.Should().Contain($"OriginalPath={sourceFile}");
            
            _mockHistory.Verify(h => h.UpdateThreatStatusAsync(sourceFile, "Quarantined"), Times.Once);
        }

        [Fact]
        public async Task RestoreQuarantinedFile_ShouldMoveFileBackToOriginalLocation()
        {
            // Arrange
            string originalFile = Path.Combine(_tempSourcePath, "restore_me.txt");
            File.WriteAllText(originalFile, "Contents to be restored.");
            
            await _service.QuarantineFile(originalFile);
            string quarantinedFile = Directory.GetFiles(_testQuarantinePath, "*.quarantine")[0];

            // Act
            await _service.RestoreQuarantinedFile(quarantinedFile);

            // Assert
            File.Exists(originalFile).Should().BeTrue();
            File.ReadAllText(originalFile).Should().Be("Contents to be restored.");
            File.Exists(quarantinedFile).Should().BeFalse();
            File.Exists(quarantinedFile + ".metadata").Should().BeFalse();
        }

        [Fact]
        public async Task DeleteQuarantinedFile_ShouldRemoveFileAndMetadataPermanently()
        {
            // Arrange
            string fileToDelete = Path.Combine(_tempSourcePath, "delete_me.txt");
            File.WriteAllText(fileToDelete, "Goodbye world.");
            
            await _service.QuarantineFile(fileToDelete);
            string quarantinedFile = Directory.GetFiles(_testQuarantinePath, "*.quarantine")[0];

            // Act
            await _service.DeleteQuarantinedFile(quarantinedFile);

            // Assert
            File.Exists(quarantinedFile).Should().BeFalse();
            File.Exists(quarantinedFile + ".metadata").Should().BeFalse();
        }

        public void Dispose()
        {
            if (Directory.Exists(_testQuarantinePath)) Directory.Delete(_testQuarantinePath, true);
            if (Directory.Exists(_tempSourcePath)) Directory.Delete(_tempSourcePath, true);
        }
    }
}
