using System;
using System.Collections.Generic;
using System.Threading;
using FluentAssertions;
using RansomGuard.Service.Engine;
using Xunit;

namespace RansomGuard.Tests.Engine
{
    public class MassEncryptionDetectorTests
    {
        private readonly MassEncryptionDetector _detector;

        public MassEncryptionDetectorTests()
        {
            _detector = new MassEncryptionDetector();
        }

        // --- No alert below threshold ---

        [Fact]
        public void RecordFileChange_BelowThreshold_ShouldNotFireAlert()
        {
            bool alertFired = false;
            _detector.MassEncryptionDetected += _ => alertFired = true;

            // High sensitivity (level 3) threshold = 10; add 9 changes
            for (int i = 0; i < 9; i++)
                _detector.RecordFileChange($"proc{i}", i, $"C:\\file{i}.txt", false, 1.0, sensitivityLevel: 3);

            alertFired.Should().BeFalse();
        }

        // --- Alert fires at threshold ---

        [Fact]
        public void RecordFileChange_AtThreshold_ShouldFireAlert()
        {
            MassEncryptionAlert? receivedAlert = null;
            _detector.MassEncryptionDetected += alert => receivedAlert = alert;

            // High sensitivity (level 3) threshold = 10
            for (int i = 0; i < 10; i++)
                _detector.RecordFileChange("malware", 1234, $"C:\\file{i}.txt", false, 1.0, sensitivityLevel: 3);

            receivedAlert.Should().NotBeNull();
            receivedAlert!.Reason.Should().Be("High volume of file modifications");
            receivedAlert.ProcessName.Should().Be("malware");
            receivedAlert.ProcessId.Should().Be(1234);
        }

        // --- Suspicious cluster heuristic ---

        [Fact]
        public void RecordFileChange_SuspiciousCluster_ShouldFireAlertBeforeVelocityThreshold()
        {
            MassEncryptionAlert? receivedAlert = null;
            _detector.MassEncryptionDetected += alert => receivedAlert = alert;

            // High sensitivity threshold = 10; add only 6 changes but all suspicious
            // Cluster heuristic: suspiciousChanges >= threshold/2 (5) AND >= 3
            for (int i = 0; i < 6; i++)
                _detector.RecordFileChange("ransomware", 999, $"C:\\file{i}.crypt", isSuspicious: true, entropy: 7.9, sensitivityLevel: 3);

            receivedAlert.Should().NotBeNull();
            receivedAlert!.Reason.Should().Be("Cluster of suspicious file activities");
        }

        // --- High entropy heuristic ---

        [Fact]
        public void RecordFileChange_HighEntropyWrites_ShouldFireAlert()
        {
            MassEncryptionAlert? receivedAlert = null;
            _detector.MassEncryptionDetected += alert => receivedAlert = alert;

            // High entropy heuristic: >= 5 writes with entropy > 7.5
            for (int i = 0; i < 5; i++)
                _detector.RecordFileChange("encryptor", 777, $"C:\\file{i}.dat", isSuspicious: false, entropy: 7.8, sensitivityLevel: 3);

            receivedAlert.Should().NotBeNull();
            receivedAlert!.Reason.Should().Be("Multiple high-entropy data writes");
        }

        // --- Alert includes files to quarantine ---

        [Fact]
        public void RecordFileChange_Alert_ShouldIncludeAffectedFiles()
        {
            MassEncryptionAlert? receivedAlert = null;
            _detector.MassEncryptionDetected += alert => receivedAlert = alert;

            for (int i = 0; i < 10; i++)
                _detector.RecordFileChange("malware", 1234, $"C:\\docs\\file{i}.txt", false, 1.0, sensitivityLevel: 3);

            receivedAlert.Should().NotBeNull();
            receivedAlert!.FilesToQuarantine.Should().HaveCountGreaterThan(0);
        }

        // --- Queue clears after alert ---

        [Fact]
        public void RecordFileChange_AfterAlert_QueueShouldBeCleared()
        {
            int alertCount = 0;
            _detector.MassEncryptionDetected += _ => alertCount++;

            // Trigger first alert
            for (int i = 0; i < 10; i++)
                _detector.RecordFileChange("malware", 1, $"C:\\file{i}.txt", false, 1.0, sensitivityLevel: 3);

            alertCount.Should().Be(1);

            // Add 9 more — should NOT trigger a second alert since queue was cleared
            for (int i = 10; i < 19; i++)
                _detector.RecordFileChange("malware", 1, $"C:\\file{i}.txt", false, 1.0, sensitivityLevel: 3);

            alertCount.Should().Be(1);
        }

        // --- Sensitivity levels ---

        [Theory]
        [InlineData(1, 30)] // Low: threshold * 3
        [InlineData(2, 20)] // Medium: threshold * 2
        [InlineData(4, 5)]  // Paranoid: 5
        public void RecordFileChange_SensitivityLevel_AffectsThreshold(int sensitivity, int threshold)
        {
            MassEncryptionAlert? receivedAlert = null;
            _detector.MassEncryptionDetected += alert => receivedAlert = alert;

            // Add threshold - 1 changes: should NOT fire
            for (int i = 0; i < threshold - 1; i++)
                _detector.RecordFileChange("proc", i, $"C:\\file{i}.txt", false, 1.0, sensitivity);

            receivedAlert.Should().BeNull($"threshold is {threshold}, added {threshold - 1}");

            // Add one more: should fire
            _detector.RecordFileChange("proc", threshold, $"C:\\file{threshold}.txt", false, 1.0, sensitivity);
            receivedAlert.Should().NotBeNull($"threshold is {threshold}, added {threshold}");
        }

        // --- GetAdditionalFilesByProcess ---

        [Fact]
        public void GetAdditionalFilesByProcess_ShouldReturnFilesForMatchingProcess()
        {
            // Add some changes without triggering alert (below threshold)
            _detector.RecordFileChange("malware", 1234, "C:\\file1.txt", false, 1.0, sensitivityLevel: 1);
            _detector.RecordFileChange("malware", 1234, "C:\\file2.txt", false, 1.0, sensitivityLevel: 1);
            _detector.RecordFileChange("other", 9999, "C:\\other.txt", false, 1.0, sensitivityLevel: 1);

            var files = _detector.GetAdditionalFilesByProcess(1234, "malware");

            files.Should().Contain("C:\\file1.txt");
            files.Should().Contain("C:\\file2.txt");
            files.Should().NotContain("C:\\other.txt");
        }

        // --- ClearRecentChanges ---

        [Fact]
        public void ClearRecentChanges_ShouldPreventSubsequentAlerts()
        {
            int alertCount = 0;
            _detector.MassEncryptionDetected += _ => alertCount++;

            // Add 9 changes (below threshold of 10 at level 3)
            for (int i = 0; i < 9; i++)
                _detector.RecordFileChange("proc", 1, $"C:\\file{i}.txt", false, 1.0, sensitivityLevel: 3);

            _detector.ClearRecentChanges();

            // Add 9 more — still below threshold since queue was cleared
            for (int i = 9; i < 18; i++)
                _detector.RecordFileChange("proc", 1, $"C:\\file{i}.txt", false, 1.0, sensitivityLevel: 3);

            alertCount.Should().Be(0);
        }

        // --- Alert timestamp ---

        [Fact]
        public void RecordFileChange_Alert_ShouldHaveRecentTimestamp()
        {
            MassEncryptionAlert? receivedAlert = null;
            _detector.MassEncryptionDetected += alert => receivedAlert = alert;

            var before = DateTime.Now;
            for (int i = 0; i < 10; i++)
                _detector.RecordFileChange("malware", 1, $"C:\\file{i}.txt", false, 1.0, sensitivityLevel: 3);
            var after = DateTime.Now;

            receivedAlert.Should().NotBeNull();
            receivedAlert!.Timestamp.Should().BeOnOrAfter(before).And.BeOnOrBefore(after);
        }
    }
}
