using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;
using RansomGuard.Core.Services;
using RansomGuard.Core.Interfaces;
using RansomGuard.Core.Models;
using RansomGuard.Core.Helpers;
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

        [RelayCommand]
        public void Refresh()
        {
            LoadData();
        }

        [RelayCommand]
        private void ExportToCsv()
        {
            try
            {
                if (_monitorService == null)
                {
                    MessageBox.Show("No data source is connected. Please ensure the service is running.",
                        "Export Unavailable", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                var exportDir = Path.Combine(
                    PathConfiguration.GetConfigDirectory(),
                    "Exports");
                Directory.CreateDirectory(exportDir);

                var fileName = $"RansomGuard_Report_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.csv";
                var filePath = Path.Combine(exportDir, fileName);

                var threats = (_monitorService?.GetRecentThreats() ?? Enumerable.Empty<Threat>()).Where(t => t != null).ToList();
                var activities = (_monitorService?.GetRecentFileActivities() ?? Enumerable.Empty<FileActivity>()).Where(a => a != null).ToList();

                var sb = new StringBuilder();

                // --- Threats Section ---
                sb.AppendLine("=== THREAT DETECTIONS ===");
                sb.AppendLine("Timestamp,Severity,Name,Path,Process,Description");
                foreach (var t in threats.OrderByDescending(x => x.Timestamp))
                {
                    sb.AppendLine(
                        $"\"{t.Timestamp:yyyy-MM-dd HH:mm:ss}\"," +
                        $"\"{t.Severity}\"," +
                        $"\"{EscapeCsv(t.Name)}\"," +
                        $"\"{EscapeCsv(t.Path)}\"," +
                        $"\"{EscapeCsv(t.ProcessName)}\"," +
                        $"\"{EscapeCsv(t.Description)}\"");
                }

                sb.AppendLine();

                // --- File Activity Section ---
                sb.AppendLine("=== FILE ACTIVITY LOG ===");
                sb.AppendLine("Timestamp,Action,FilePath,Entropy,Suspicious,Process");
                foreach (var a in activities.OrderByDescending(x => x.Timestamp))
                {
                    sb.AppendLine(
                        $"\"{a.Timestamp:yyyy-MM-dd HH:mm:ss}\"," +
                        $"\"{EscapeCsv(a.Action)}\"," +
                        $"\"{EscapeCsv(a.FilePath)}\"," +
                        $"{a.Entropy:F2}," +
                        $"{a.IsSuspicious}," +
                        $"\"{EscapeCsv(a.ProcessName)}\"");
                }

                // --- Summary Section ---
                sb.AppendLine();
                sb.AppendLine("=== SUMMARY ===");
                sb.AppendLine($"Report Generated,{DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                sb.AppendLine($"Total Threats,{threats.Count}");
                sb.AppendLine($"Total File Events,{activities.Count}");
                sb.AppendLine($"Security Score,{SecurityScore}");
                sb.AppendLine($"Weekly Detections,{WeeklyDetectionsCount}");
                sb.AppendLine($"Last Scan,{LastScanDate}");
                sb.AppendLine($"Total Scans,{TotalScans}");

                File.WriteAllText(filePath, sb.ToString(), Encoding.UTF8);

                // Open the folder in Explorer so user can find the file
                System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{filePath}\"");

                MessageBox.Show(
                    $"Report exported successfully!\n\nLocation: {filePath}",
                    "Export Complete", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"Export failed: {ex.Message}",
                    "Export Error", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        [RelayCommand]
        private void ExportToPdf()
        {
            // PDF export requires a third-party library (QuestPDF or PdfSharp).
            // See FUTURE_BACKLOG.md #26 for implementation notes.
            // For now, prompt the user to use CSV and track the backlog item.
            var result = MessageBox.Show(
                "PDF export requires an additional library (QuestPDF) and is planned for a future release.\n\n" +
                "Would you like to export a CSV report instead?",
                "PDF Export – Coming Soon", MessageBoxButton.YesNo, MessageBoxImage.Information);

            if (result == MessageBoxResult.Yes)
                ExportToCsvCommand.Execute(null);
        }

        private static string EscapeCsv(string? value)
            => (value ?? string.Empty).Replace("\"", "\"\"");

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
                var allThreats = (_monitorService.GetRecentThreats() ?? Enumerable.Empty<Threat>())
                    .Where(t => t != null)
                    .ToList();
                
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
                    var commonType = thisWeekThreats.GroupBy(t => t?.Name ?? "Unknown")
                        .OrderByDescending(g => g.Count())
                        .FirstOrDefault();
                    
                    if (commonType != null)
                    {
                        MostCommonThreatType = commonType.Key;
                        int percent = (int)((double)commonType.Count() / thisWeekThreats.Count * 100);
                        CommonThreatDetail = $"Detected in {percent}% of events";
                    }
                }

                // 3. Detection Series (Smooth Spline Area Chart)
                var dailyStats = allThreats
                    .Where(t => t != null && t.Timestamp >= now.AddDays(-30))
                    .GroupBy(t => t.Timestamp.Date)
                    .Select(g => new { Date = g.Key, Count = g.Count() })
                    .OrderBy(x => x.Date)
                    .ToList();

                MonthlyDetectionsCount = allThreats.Count(t => t.Timestamp >= now.AddDays(-30)).ToString("N0");
                PeakDetectionCount = dailyStats.Any() ? dailyStats.Max(x => x.Count).ToString("N0") : "0";
                AverageDetectionsCount = dailyStats.Any() ? dailyStats.Average(x => x.Count).ToString("F1") : "0.0";

                // If only one data point, add a leading zero-point for better look
                if (dailyStats.Count == 1)
                {
                    dailyStats.Insert(0, new { Date = dailyStats[0].Date.AddDays(-1), Count = 0 });
                }

                if (DetectionSeries != null)
                {
                    DetectionSeries.Clear();
                    DetectionSeries.Add(new LineSeries<int>
                    {
                        Values = dailyStats.Select(x => x.Count).ToArray(),
                        Name = "Total Detections",
                        GeometrySize = 8,
                        LineSmoothness = 1,
                        Stroke = new SolidColorPaint(SKColor.Parse("#4fc3f7")) { StrokeThickness = 4 },
                        Fill = new LinearGradientPaint(
                            new SKColor[] { SKColor.Parse("#4fc3f7").WithAlpha(90), SKColors.Transparent },
                            new SKPoint(0.5f, 0), new SKPoint(0.5f, 1)),
                        GeometryFill = new SolidColorPaint(SKColor.Parse("#4fc3f7")),
                        GeometryStroke = new SolidColorPaint(SKColors.White) { StrokeThickness = 2 }
                    });
                }

                XAxes.Clear();
                XAxes.Add(new Axis
                {
                    Labels = dailyStats.Select(x => x.Date.ToString("MMM dd")).ToArray(),
                    LabelsRotation = 0,
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

                // 4. Distribution Series
                var categoryStats = allThreats
                    .Where(t => t != null)
                    .GroupBy(t => t.Name ?? "Unknown")
                    .Select(g => new { Name = g.Key, Count = g.Count() })
                    .OrderByDescending(x => x.Count)
                    .Take(5)
                    .ToList();

                var palette = new[] { "#4fc3f7", "#66bb6a", "#ffa726", "#ab47bc", "#26a69a" };

                DistributionSeries.Clear();
                int colorIndex = 0;
                foreach (var stat in categoryStats)
                {
                    DistributionSeries.Add(new PieSeries<int>
                    {
                        Values = new int[] { stat.Count },
                        Name = stat.Name,
                        InnerRadius = 50,
                        Fill = new SolidColorPaint(SKColor.Parse(palette[colorIndex % palette.Length])),
                        DataLabelsPosition = LiveChartsCore.Measure.PolarLabelsPosition.Middle,
                        DataLabelsPaint = new SolidColorPaint(SKColors.Transparent),
                    });
                    colorIndex++;
                }

                // 5. Recent Significant Threats
                RecentSignificantThreats.Clear();
                foreach (var threat in allThreats.OrderByDescending(t => t.Timestamp).Take(3))
                {
                    RecentSignificantThreats.Add(threat);
                }
            }
            catch (Exception ex)
            {
                FileLogger.LogError("ui_error.log", "[ReportsViewModel] PopulateReportData error", ex);
            }
        }

        private void CalculateSecurityScore()
        {
            if (_monitorService == null)
            {
                SecurityScore = 85;
                return;
            }

            try
            {
                var telemetry = _monitorService?.GetTelemetry() ?? new TelemetryData();
                var threats = (_monitorService?.GetRecentThreats() ?? Enumerable.Empty<Threat>()).Where(t => t != null).ToList();

                int score = 100;
                int criticalThreats = threats.Count(t => t != null && t.Severity == Core.Models.ThreatSeverity.Critical);
                int highThreats = threats.Count(t => t != null && t.Severity == Core.Models.ThreatSeverity.High);
                int mediumThreats = threats.Count(t => t != null && t.Severity == Core.Models.ThreatSeverity.Medium);

                score -= criticalThreats * 15;
                score -= highThreats * 8;
                score -= mediumThreats * 3;

                if (telemetry.EntropyScore > 5.5) score -= 10;
                else if (telemetry.EntropyScore > 4.5) score -= 5;

                if (telemetry.IsHoneyPotActive) score += 2;
                if (telemetry.IsVssShieldActive) score += 2;

                SecurityScore = Math.Clamp(score, 0, 100);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ReportsViewModel] CalculateSecurityScore error: {ex.Message}");
                SecurityScore = 85; // Default fallback score
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
