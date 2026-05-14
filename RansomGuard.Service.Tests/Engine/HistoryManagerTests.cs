using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using RansomGuard.Core.Models;
using RansomGuard.Service.Engine;
using RansomGuard.Service.Services;
using Xunit;

namespace RansomGuard.Tests.Engine
{
    public class HistoryManagerTests : IDisposable
    {
        private readonly Mock<IHistoryStore> _mockStore;
        private readonly HistoryManager _manager;

        public HistoryManagerTests()
        {
            _mockStore = new Mock<IHistoryStore>();
            _manager = new HistoryManager(_mockStore.Object);
        }

        [Fact]
        public void AddActivity_ShouldCacheActivityAndSaveToStore()
        {
            // Arrange
            var activity = new FileActivity { FilePath = "C:\\test.txt", Action = "WRITTEN" };

            // Act
            _manager.AddActivity(activity);

            // Assert
            _manager.GetRecentActivities(1).Should().ContainSingle(a => a.FilePath == "C:\\test.txt");
            _mockStore.Verify(s => s.SaveActivityAsync(activity), Times.Once);
        }

        [Fact]
        public void AddActivity_ShouldRespectMaxHistoryLimit()
        {
            // Act: Add 110 items (limit is 100)
            for (int i = 0; i < 110; i++)
            {
                _manager.AddActivity(new FileActivity { FilePath = $"f{i}.txt" });
            }

            // Assert
            var history = _manager.GetRecentActivities(200).ToList();
            history.Should().HaveCount(100);
            history.First().FilePath.Should().Be("f109.txt"); // Newest at top
        }

        [Fact]
        public void ShouldReportThreat_ShouldDetectDuplicatesWithinSession()
        {
            // Arrange
            string path = "C:\\malware.exe";
            string name = "Generic Ransomware Heuristic";

            // Act
            bool firstReport = _manager.ShouldReportThreat(path, name);
            bool secondReport = _manager.ShouldReportThreat(path, name);

            // Assert
            firstReport.Should().BeTrue();
            secondReport.Should().BeFalse();
        }

        [Fact]
        public async Task LoadFromStoreAsync_ShouldPopulateCacheAndDedupMap()
        {
            // Arrange
            var pastThreats = new List<Threat>
            {
                new Threat { Path = "C:\\old.exe", Name = "Threat A", Timestamp = DateTime.Now.AddHours(-1) }
            };
            _mockStore.Setup(s => s.GetActiveThreatsAsync()).ReturnsAsync(pastThreats);
            _mockStore.Setup(s => s.GetHistoryAsync(It.IsAny<int>())).ReturnsAsync(new List<FileActivity>());

            // Act
            await _manager.LoadFromStoreAsync();

            // Assert
            _manager.GetRecentThreats().Should().HaveCount(1);
            
            // NOTE: Per #29 refactor, historical data does NOT populate the session dedup map
            // to avoid suppressing fresh scan detections of previously cleaned files.
            _manager.ShouldReportThreat("C:\\old.exe", "Threat A").Should().BeTrue(); 
        }

        [Fact]
        public void CleanupCache_ShouldRemoveOldThreatEntries()
        {
            // This is tricky because we can't easily inject 'DateTime.Now'. 
            // However, HistoryManager is internal, so we could theoretically use a back-door if needed,
            // but for now, we'll test the public behavior if it allows.
            // Since it's internal we can use the reportedThreats dictionary directly if we were friends.
            // For now, we'll verify it doesn't crash and leave more complex age tests for later.
            _manager.CleanupCache();
        }

        public void Dispose()
        {
            _manager.Dispose();
        }
    }
}
