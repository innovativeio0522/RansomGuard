using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using RansomGuard.Core.Models;

namespace RansomGuard.Converters
{
    public class SeverityToBrushConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is ThreatSeverity severity)
            {
                string key = severity switch
                {
                    ThreatSeverity.Low => "PrimaryBrush",
                    ThreatSeverity.Medium => "SecondaryBrush",
                    ThreatSeverity.High => "TertiaryBrush",
                    ThreatSeverity.Critical => "ErrorBrush",
                    _ => "OnSurfaceVariantBrush"
                };

                // Safe resource access with null guard
                if (Application.Current?.Resources.Contains(key) == true)
                {
                    return Application.Current.Resources[key];
                }
            }
            
            // Fallback to neutral brush
            return Application.Current?.TryFindResource("OnSurfaceVariantBrush") ?? Brushes.Gray;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
