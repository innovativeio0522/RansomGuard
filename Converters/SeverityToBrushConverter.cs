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
                    var resource = Application.Current.Resources[key];
                    
                    if (parameter != null && parameter.ToString() == "ColorOnly" && resource is SolidColorBrush scb)
                    {
                        return scb.Color;
                    }
                    
                    return resource;
                }
            }
            
            // Fallback
            var fallback = Application.Current?.TryFindResource("OnSurfaceVariantBrush");
            if (parameter != null && parameter.ToString() == "ColorOnly" && fallback is SolidColorBrush fscb)
            {
                return fscb.Color;
            }
            
            return fallback ?? Brushes.Gray;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
