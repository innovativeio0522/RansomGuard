using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using RansomGuard.Core.Services;
using RansomGuard.Service.Engine;
using Xunit;

namespace RansomGuard.Tests.Engine
{
    public class FileSystemMonitoringServiceTests : IDisposable
    {
        private readonly Mock<IEntropyAnalyzer> _mockEntropy;
        private readonly FileSystemMonitoringService _service;
        private readonly string _testDir;

        public FileSystemMonitoringServiceTests()
        {
            _mockEntropy = new Mock<IEntropyAnalyzer>();
            _mockEntropy.Setup(e => e.IsSuspiciousExtension(It.IsAny<string>())).Returns(false);
            _service = new FileSystemMonitoringService(_mockEntropy.Object);

            _testDir = Path.Combine(Path.GetTempPath(), "RG_FSM_Test_" + Guid.NewGuid());
            Directory.CreateDirectory(_testDir);
        }

        // --- Constructor ---

        [Fact]
        public void Constructor_NullEntropyAnalyzer_ShouldThrow()
        {
            Action act = () => new FileSystemMonitoringService(null!);
            act.Should().Throw<ArgumentNullException>().WithParameterName("entropyAnalyzer");
        }

        // --- InitializeWatchers ---

        [Fact]
        public void InitializeWatchers_RealTimeProtectionDisabled_ShouldCreateNoWatchers()
        {
            _service.InitializeWatchers(
                realTimeProtectionEnabled: false,
                standardPaths: new[] { _testDir },
                customPaths: Array.Empty<string>());

            _service.GetTotalWatcherCount().Should().Be(0);
            _service.GetActiveWatcherCount().Should().Be(0);
        }

        [Fact]
        public void InitializeWatchers_ValidPath_ShouldCreateWatcher()
        {
            _service.InitializeWatchers(
                realTimeProtectionEnabled: true,
                standardPaths: new[] { _testDir },
                customPaths: Array.Empty<string>());

            _service.GetTotalWatcherCount().Should().Be(1);
            _service.GetActiveWatcherCount().Should().Be(1);
        }

        [Fact]
        public void InitializeWatchers_NonExistentPath_ShouldSkipIt()
        {
            _service.InitializeWatchers(
                realTimeProtectionEnabled: true,
                standardPaths: new[] { @"C:\NonExistentPath_RG_Test_12345" },
                customPaths: Array.Empty<string>());

            _service.GetTotalWatcherCount().Should().Be(0);
        }

        [Fact]
        public void InitializeWatchers_DuplicatePaths_ShouldDeduplicateWatchers()
        {
            _service.InitializeWatchers(
                realTimeProtectionEnabled: true,
                standardPaths: new[] { _testDir, _testDir },
                customPaths: new[] { _testDir });

            // All three are the same path — should only create 1 watcher
            _service.GetTotalWatcherCount().Should().Be(1);
        }

        [Fact]
        public void InitializeWatchers_CalledTwice_ShouldReplaceWatchers()
        {
            string dir2 = Path.Combine(Path.GetTempPath(), "RG_FSM_Test2_" + Guid.NewGuid());
            Directory.CreateDirectory(dir2);

            try
            {
                _service.InitializeWatchers(true, new[] { _testDir }, Array.Empty<string>());
                _service.GetTotalWatcherCount().Should().Be(1);

                _service.InitializeWatchers(true, new[] { _testDir, dir2 }, Array.Empty<string>());
                _service.GetTotalWatcherCount().Should().Be(2);
            }
            finally
            {
                Directory.Delete(dir2);
            }
        }

        // --- GetMonitoredPaths ---

        [Fact]
        public void GetMonitoredPaths_ShouldReturnWatchedDirectories()
        {
            _service.InitializeWatchers(true, new[] { _testDir }, Array.Empty<string>());

            var paths = _service.GetMonitoredPaths().ToList();
            paths.Should().HaveCount(1);
            paths[0].Should().Be(_testDir);
        }

        // --- File event detection ---

        [Fact]
        public async Task FileEventDetected_ShouldFireWhenFileIsCreated()
        {
            _service.InitializeWatchers(true, new[] { _testDir }, Array.Empty<string>());

            FileSystemEvent? receivedEvent = null;
            var tcs = new TaskCompletionSource<bool>();
            _service.FileEventDetected += e =>
            {
                receivedEvent = e;
                tcs.TrySetResult(true);
            };

            string testFile = Path.Combine(_testDir, "test_" + Guid.NewGuid() + ".txt");
            File.WriteAllText(testFile, "hello");

            await Task.WhenAny(tcs.Task, Task.Delay(2000));

            receivedEvent.Should().NotBeNull();
            receivedEvent!.Path.Should().Be(testFile);
            // FSW may fire CREATED or CHANGED depending on OS timing — both are valid
            receivedEvent.Action.Should().BeOneOf("CREATED", "CHANGED");
        }

        [Fact]
        public async Task FileEventDetected_ShouldFlagHoneypotFiles()
        {
            _mockEntropy.Setup(e => e.IsSuspiciousExtension(It.IsAny<string>())).Returns(false);
            _service.InitializeWatchers(true, new[] { _testDir }, Array.Empty<string>());

            FileSystemEvent? receivedEvent = null;
            var tcs = new TaskCompletionSource<bool>();
            _service.FileEventDetected += e =>
            {
                if (e.Path.Contains("!$RansomGuard_Bait"))
                {
                    receivedEvent = e;
                    tcs.TrySetResult(true);
                }
            };

            string baitFile = Path.Combine(_testDir, "!$RansomGuard_Bait_test.txt");
            File.WriteAllText(baitFile, "bait");

            await Task.WhenAny(tcs.Task, Task.Delay(2000));

            receivedEvent.Should().NotBeNull();
            receivedEvent!.IsSuspicious.Should().BeTrue();
        }

        // --- Dispose ---

        [Fact]
        public void Dispose_ShouldClearAllWatchers()
        {
            _service.InitializeWatchers(true, new[] { _testDir }, Array.Empty<string>());
            _service.GetTotalWatcherCount().Should().Be(1);

            _service.Dispose();

            _service.GetTotalWatcherCount().Should().Be(0);
        }

        public void Dispose()
        {
            _service.Dispose();
            if (Directory.Exists(_testDir))
                Directory.Delete(_testDir, recursive: true);
        }
    }
}
