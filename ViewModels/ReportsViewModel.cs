using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using RansomGuard.Core.Services;
using RansomGuard.Core.Interfaces;
using RansomGuard.Core.Models;
using LiveChartsCore;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using SkiaSharp;
using System.Windows;
using RansomGuard.Core.IPC;

namespace RansomGuard.ViewModels
{
    public partial class ReportsViewModel : ViewModelBase, IDisposable
    {
        private readonly ISystemMonitorService? _monitorService;

        [ObservableProperty]
        private string _lastScanDate = "Never";

        [ObservableProperty]
        private int _totalScans = 0;

        [ObservableProperty]
        private int _securityScore = 0;

        [ObservableProperty]
        private string _weeklyDetectionsCount = "0";

        [ObservableProperty]
        private string _weeklyDetectionsTrendText = "0% vs last week";

        [ObservableProperty]
        private string _mostCommonThreatType = "None";

        [ObservableProperty]
        private string _commonThreatDetail = "No threats detected";

        [ObservableProperty]
        private string _monthlyDetectionsCount = "0";

        [ObservableProperty]
        private string _peakDetectionCount = "0";

        [ObservableProperty]
        private string _averageDetectionsCount = "0";

        public ObservableCollection<ISeries> DetectionSeries { get; set; } = new();
        public ObservableCollection<ISeries> DistributionSeries { get; set; } = new();
        public ObservableCollection<Axis> XAxes { get; set; } = new();
        public ObservableCollection<Axis> YAxes { get; set; } = new();
        public SolidColorPaint LegendTextPaint { get; set; } = new(SKColor.Parse("#8a92a6"));

        public ObservableCollection<Threat> RecentSignificantThreats { get; set; } = new();

        public ReportsViewModel(ISystemMonitorService? monitorService = null)
        {
            _monitorService = monitorService;
            
            if (_monitorService != null)
            {
                _monitorService.ConnectionStatusChanged += OnConnectionStatusChanged;
                _monitorService.TelemetryUpdated += OnTelemetryUpdated;
                _monitorService.ThreatDetected += OnThreatDetected;
            }

            LoadData();
        }

        private void OnConnectionStatusChanged(bool connected)
        {
            if (connected)
            {
                System.Windows.Application.Current.Dispatcher.Invoke(LoadData);
            }
        }

        private void OnTelemetryUpdated(TelemetryData telemetry)
        {
            // Only update metrics that don't trigger chart re-renders
            System.Windows.Application.Current.Dispatcher.Invoke(() => {
                CalculateSecurityScore();
                // Update scan date/scans count if configuration changed
                var lastScan = ConfigurationService.Instance.LastScanTime;
                if (lastScan != DateTime.MinValue)
                {
                    LastScanDate = lastScan.ToString("MMM dd, yyyy HH:mm");
                    TotalScans = ConfigurationService.Instance.TotalScansCount;
                }
            });
        }

        private void OnThreatDetected(Threat threat)
        {
            System.Windows.Application.Current.Dispatcher.Invoke(LoadData);
        }

        [CommunityToolkit.Mvvm.Input.RelayCommand]
        public void Refresh()
        {
            LoadData();
        }

        private void LoadData()
        {
            // Load last scan time from configuration
            var lastScan = ConfigurationService.Instance.LastScanTime;
            if (lastScan != DateTime.MinValue)
            {
                LastScanDate = lastScan.ToString("MMM dd, yyyy HH:mm");
                TotalScans = ConfigurationService.Instance.TotalScansCount;
            }
            else
            {
                LastScanDate = "Never";
                TotalScans = 0;
            }

            // Calculate security score based on threat data and entropy
            CalculateSecurityScore();
            
            // Populate dynamic report data
            PopulateReportData();
        }

        private void PopulateReportData()
        {
            if (_monitorService == null) return;

            try
            {
                var allThreats = _monitorService.GetRecentThreats().ToList();
                var now = DateTime.Now;
                var sevenDaysAgo = now.AddDays(-7);
                var fourteenDaysAgo = now.AddDays(-14);

                // 1. Weekly Detections & Trend
                var thisWeekThreats = allThreats.Where(t => t.Timestamp >= sevenDaysAgo).ToList();
                var lastWeekThreats = allThreats.Where(t => t.Timestamp >= fourteenDaysAgo && t.Timestamp < sevenDaysAgo).ToList();

                WeeklyDetectionsCount = thisWeekThreats.Count.ToString("N0");
                
                if (lastWeekThreats.Count > 0)
                {
                    double trend = ((double)(thisWeekThreats.Count - lastWeekThreats.Count) / lastWeekThreats.Count) * 100;
                    WeeklyDetectionsTrendText = $"{Math.Abs(trend):F1}% {(trend >= 0 ? "increase" : "decrease")} vs last week";
                }
                else
                {
                    WeeklyDetectionsTrendText = "First week of data";
                }

                // 2. Most Common Threat Type
                if (thisWeekThreats.Any())
                {
                    var commonType = thisWeekThreats.GroupBy(t => t.Name)
                        .OrderByDescending(g => g.Count())
                        .First();
                    
                    MostCommonThreatType = commonType.Key;
                    int percent = (int)((double)commonType.Count() / thisWeekThreats.Count * 100);
                    CommonThreatDetail = $"Detected in {percent}% of events";
                }

                // 3. Detection Series (Smooth Spline Area Chart)
                var dailyStats = allThreats
                    .Where(t => t.Timestamp >= now.AddDays(-30))
                    .GroupBy(t => t.Timestamp.Date)
                    .Select(g => new { Date = g.Key, Count = g.Count() })
                    .OrderBy(x => x.Date)
                    .ToList();

                // Calculate Month-over-Month Metrics
                int totalMonth = dailyStats.Sum(x => x.Count);
                int peakDay = dailyStats.Any() ? dailyStats.Max(x => x.Count) : 0;
                double avgDay = dailyStats.Any() ? (double)totalMonth / 30 : 0;

                MonthlyDetectionsCount = totalMonth.ToString("N0");
                PeakDetectionCount = peakDay.ToString("N0");
                AverageDetectionsCount = avgDay.ToString("F1");

                // If only one data point, add a leading zero-point for better look
                if (dailyStats.Count == 1)
                {
                    dailyStats.Insert(0, new { Date = dailyStats[0].Date.AddDays(-1), Count = 0 });
                }

                DetectionSeries.Clear();
                DetectionSeries.Add(new LineSeries<int>
                {
                    Values = dailyStats.Select(x => x.Count).ToArray(),
                    Name = "Total Detections",
                    GeometrySize = 8,
                    LineSmoothness = 1, // Spline smoothness 0-1
                    Stroke = new SolidColorPaint(SKColor.Parse("#4fc3f7")) { StrokeThickness = 4 },
                    Fill = new LinearGradientPaint(
                        new SKColor[] { SKColor.Parse("#4fc3f7").WithAlpha(90), SKColors.Transparent },
                        new SKPoint(0.5f, 0), new SKPoint(0.5f, 1)),
                    GeometryFill = new SolidColorPaint(SKColor.Parse("#4fc3f7")),
                    GeometryStroke = new SolidColorPaint(SKColors.White) { StrokeThickness = 2 }
                });

                XAxes.Clear();
                XAxes.Add(new Axis
                {
                    Labels = dailyStats.Select(x => x.Date.ToString("MMM dd")).ToArray(),
                    LabelsRotation = 0, // Keep it flat for premium look
                    TextSize = 10,
                    LabelsPaint = new SolidColorPaint(SKColor.Parse("#8a92a6")),
                    SeparatorsPaint = new SolidColorPaint(SKColor.Parse("#1affffff")) { StrokeThickness = 1 }
                });

                YAxes.Clear();
                YAxes.Add(new Axis
                {
                    MinLimit = 0,
                    TextSize = 10,
                    LabelsPaint = new SolidColorPaint(SKColor.Parse("#8a92a6")),
                    SeparatorsPaint = new SolidColorPaint(SKColor.Parse("#1affffff")) { StrokeThickness = 1 }
                });

                // 4. Distribution Series (Premium Doughnut Chart)
                var categoryStats = allThreats
                    .GroupBy(t => t.Name)
                    .Select(g => new { Name = g.Key, Count = g.Count() })
                    .OrderByDescending(x => x.Count)
                    .Take(5)
                    .ToList();

                // Define a premium palette
                var palette = new[] { "#4fc3f7", "#66bb6a", "#ffa726", "#ab47bc", "#26a69a" };

                DistributionSeries.Clear();
                int colorIndex = 0;
                foreach (var stat in categoryStats)
                {
                    DistributionSeries.Add(new PieSeries<int>
                    {
                        Values = new int[] { stat.Count },
                        Name = stat.Name,
                        InnerRadius = 50, // Thicker ring for better visual presence
                        Fill = new SolidColorPaint(SKColor.Parse(palette[colorIndex % palette.Length])),
                        DataLabelsPosition = LiveChartsCore.Measure.PolarLabelsPosition.Middle,
                        DataLabelsPaint = new SolidColorPaint(SKColors.Transparent), // Hide labels inside
                    });
                    colorIndex++;
                }

                // 5. Recent Significant Threats (Timeline)
                RecentSignificantThreats.Clear();
                foreach (var threat in allThreats.OrderByDescending(t => t.Timestamp).Take(3))
                {
                    RecentSignificantThreats.Add(threat);
                }
            }
            catch
            {
                // Fallback or log
            }
        }

        private void CalculateSecurityScore()
        {
            if (_monitorService == null)
            {
                SecurityScore = 85; // Default score when no monitor service
                return;
            }

            try
            {
                var telemetry = _monitorService.GetTelemetry();
                var threats = _monitorService.GetRecentThreats().ToList();

                // Start with perfect score
                int score = 100;

                // Deduct points for threats
                int criticalThreats = threats.Count(t => t.Severity == Core.Models.ThreatSeverity.Critical);
                int highThreats = threats.Count(t => t.Severity == Core.Models.ThreatSeverity.High);
                int mediumThreats = threats.Count(t => t.Severity == Core.Models.ThreatSeverity.Medium);

                score -= criticalThreats * 15; // -15 per critical threat
                score -= highThreats * 8;      // -8 per high threat
                score -= mediumThreats * 3;    // -3 per medium threat

                // Deduct points for high entropy (potential encryption activity)
                if (telemetry.EntropyScore > 5.5)
                {
                    score -= 10;
                }
                else if (telemetry.EntropyScore > 4.5)
                {
                    score -= 5;
                }

                // Bonus points for active shields
                if (telemetry.IsHoneyPotActive) score += 2;
                if (telemetry.IsVssShieldActive) score += 2;

                // Clamp score between 0 and 100
                SecurityScore = Math.Clamp(score, 0, 100);
            }
            catch
            {
                SecurityScore = 85; // Fallback score on error
            }
        }
        public void Dispose()
        {
            if (_monitorService != null)
            {
                _monitorService.ConnectionStatusChanged -= OnConnectionStatusChanged;
                _monitorService.TelemetryUpdated -= OnTelemetryUpdated;
                _monitorService.ThreatDetected -= OnThreatDetected;
            }
        }
    }
}
