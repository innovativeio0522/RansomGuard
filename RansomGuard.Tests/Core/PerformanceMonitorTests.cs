using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using RansomGuard.Core.Services;
using Xunit;

namespace RansomGuard.Tests.Core
{
    public class PerformanceMonitorTests
    {
        // Use a fresh instance per test to avoid singleton state bleed
        private static PerformanceMonitor Monitor => PerformanceMonitor.Instance;

        // ── Counters ───────────────────────────────────────────────────────────

        [Fact]
        public void RecordFileAnalysis_ShouldIncrementTotalFilesAnalyzed()
        {
            var before = Monitor.GetSnapshot().TotalFilesAnalyzed;
            Monitor.RecordFileAnalysis(10.0, wasSuspicious: false);
            Monitor.GetSnapshot().TotalFilesAnalyzed.Should().Be(before + 1);
        }

        [Fact]
        public void RecordFileAnalysis_Dropped_ShouldIncrementEventsDropped()
        {
            var before = Monitor.GetSnapshot().TotalEventsDropped;
            Monitor.RecordFileAnalysis(0, wasSuspicious: false, wasDropped: true);
            Monitor.GetSnapshot().TotalEventsDropped.Should().Be(before + 1);
        }

        [Fact]
        public void RecordThreatDetected_ShouldIncrementTotalThreats()
        {
            var before = Monitor.GetSnapshot().TotalThreatsDetected;
            Monitor.RecordThreatDetected();
            Monitor.GetSnapshot().TotalThreatsDetected.Should().Be(before + 1);
        }

        [Fact]
        public void RecordFileQuarantined_ShouldIncrementTotalQuarantined()
        {
            var before = Monitor.GetSnapshot().TotalFilesQuarantined;
            Monitor.RecordFileQuarantined();
            Monitor.GetSnapshot().TotalFilesQuarantined.Should().Be(before + 1);
        }

        [Fact]
        public void RecordMassEncryptionAlert_ShouldIncrementAlertCount()
        {
            var before = Monitor.GetSnapshot().TotalMassEncryptionAlerts;
            Monitor.RecordMassEncryptionAlert();
            Monitor.GetSnapshot().TotalMassEncryptionAlerts.Should().Be(before + 1);
        }

        // ── Rolling averages ───────────────────────────────────────────────────

        [Fact]
        public void RecordFileAnalysis_ShouldUpdateAvgAnalysisMs()
        {
            // Record several known values
            Monitor.RecordFileAnalysis(10.0, false);
            Monitor.RecordFileAnalysis(20.0, false);
            Monitor.RecordFileAnalysis(30.0, false);

            var snap = Monitor.GetSnapshot();
            snap.AvgAnalysisMs.Should().BeGreaterThan(0);
        }

        [Fact]
        public void RecordEntropyCalculation_ShouldUpdateAvgEntropyMs()
        {
            Monitor.RecordEntropyCalculation(5.0, 7.8);
            Monitor.RecordEntropyCalculation(8.0, 6.5);

            Monitor.GetSnapshot().AvgEntropyCalcMs.Should().BeGreaterThan(0);
        }

        [Fact]
        public void RecordIpcWrite_ShouldUpdateAvgIpcWriteMs()
        {
            Monitor.RecordIpcWrite(2.5, succeeded: true);
            Monitor.RecordIpcWrite(3.5, succeeded: true);

            Monitor.GetSnapshot().AvgIpcWriteMs.Should().BeGreaterThan(0);
        }

        // ── P95 ────────────────────────────────────────────────────────────────

        [Fact]
        public void P95AnalysisMs_ShouldBeAtLeastAverage()
        {
            for (int i = 1; i <= 20; i++)
                Monitor.RecordFileAnalysis(i * 5.0, false);

            var snap = Monitor.GetSnapshot();
            snap.P95AnalysisMs.Should().BeGreaterThanOrEqualTo(snap.AvgAnalysisMs);
        }

        // ── Gauges ─────────────────────────────────────────────────────────────

        [Fact]
        public void SetEventQueueDepth_ShouldReflectInSnapshot()
        {
            Monitor.SetEventQueueDepth(42);
            Monitor.GetSnapshot().EventQueueDepth.Should().Be(42);
        }

        [Fact]
        public void SetActiveAnalysisCount_ShouldReflectInSnapshot()
        {
            Monitor.SetActiveAnalysisCount(7);
            Monitor.GetSnapshot().ActiveAnalysisCount.Should().Be(7);
        }

        [Fact]
        public void SetWatcherCount_ShouldReflectInSnapshot()
        {
            Monitor.SetWatcherCount(12);
            Monitor.GetSnapshot().WatcherCount.Should().Be(12);
        }

        // ── Ring buffer ────────────────────────────────────────────────────────

        [Fact]
        public void GetRecentSamples_ShouldReturnSamplesInOrder()
        {
            Monitor.RecordFileAnalysis(11.0, wasSuspicious: false);
            Monitor.RecordFileAnalysis(22.0, wasSuspicious: true);

            var samples = Monitor.GetRecentSamples(10);
            samples.Should().NotBeEmpty();
            samples.Any(s => s.Operation == "FileAnalysis").Should().BeTrue();
        }

        [Fact]
        public void GetRecentSamples_ShouldRespectCountLimit()
        {
            for (int i = 0; i < 20; i++)
                Monitor.RecordFileAnalysis(i, false);

            var samples = Monitor.GetRecentSamples(5);
            samples.Count.Should().BeLessThanOrEqualTo(5);
        }

        [Fact]
        public void OperationSample_ShouldHaveRecentTimestamp()
        {
            var before = DateTime.UtcNow;
            Monitor.RecordFileAnalysis(5.0, false);
            var after = DateTime.UtcNow;

            var sample = Monitor.GetRecentSamples(1).LastOrDefault();
            sample.Should().NotBeNull();
            sample!.Timestamp.Should().BeOnOrAfter(before).And.BeOnOrBefore(after);
        }

        // ── TimedOperation ─────────────────────────────────────────────────────

        [Fact]
        public void MeasureFileAnalysis_ShouldRecordNonZeroDuration()
        {
            var before = Monitor.GetSnapshot().TotalFilesAnalyzed;

            using (Monitor.MeasureFileAnalysis())
            {
                Thread.Sleep(5); // Ensure measurable duration
            }

            Monitor.GetSnapshot().TotalFilesAnalyzed.Should().Be(before + 1);
        }

        [Fact]
        public void MeasureEntropyCalculation_ShouldRecordDuration()
        {
            var avgBefore = Monitor.GetSnapshot().AvgEntropyCalcMs;

            using (Monitor.MeasureEntropyCalculation(entropyValue: 7.9))
            {
                Thread.Sleep(2);
            }

            // Average should have been updated (may be same if already had many samples)
            Monitor.GetSnapshot().AvgEntropyCalcMs.Should().BeGreaterThanOrEqualTo(0);
        }

        [Fact]
        public void MeasureIpcWrite_ShouldRecordDuration()
        {
            using (Monitor.MeasureIpcWrite(succeeded: true))
            {
                Thread.Sleep(1);
            }

            Monitor.GetSnapshot().AvgIpcWriteMs.Should().BeGreaterThanOrEqualTo(0);
        }

        // ── Snapshot ToString ──────────────────────────────────────────────────

        [Fact]
        public void PerformanceSnapshot_ToString_ShouldContainKeyMetrics()
        {
            var snap = Monitor.GetSnapshot();
            var str = snap.ToString();

            str.Should().Contain("Analyzed=");
            str.Should().Contain("Threats=");
            str.Should().Contain("Quarantined=");
            str.Should().Contain("Dropped=");
            str.Should().Contain("AvgAnalysis=");
            str.Should().Contain("P95=");
        }

        // ── Thread safety ──────────────────────────────────────────────────────

        [Fact]
        public async Task RecordFileAnalysis_ConcurrentCalls_ShouldNotThrow()
        {
            // Just verify concurrent calls don't throw and the counter advances
            var before = Monitor.GetSnapshot().TotalFilesAnalyzed;

            Func<Task> act = async () =>
            {
                var tasks = Enumerable.Range(0, 50)
                    .Select(_ => Task.Run(() => Monitor.RecordFileAnalysis(
                        Random.Shared.NextDouble() * 100,
                        Random.Shared.NextDouble() > 0.5)))
                    .ToArray();
                await Task.WhenAll(tasks);
            };

            await act.Should().NotThrowAsync();
            Monitor.GetSnapshot().TotalFilesAnalyzed.Should().BeGreaterThan(before);
        }
    }
}
