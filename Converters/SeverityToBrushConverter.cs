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
                switch (severity)
                {
                    case ThreatSeverity.Low:
                        return Application.Current.Resources["PrimaryBrush"];
                    case ThreatSeverity.Medium:
                        return Application.Current.Resources["SecondaryBrush"];
                    case ThreatSeverity.High:
                        return Application.Current.Resources["TertiaryBrush"];
                    case ThreatSeverity.Critical:
                        return Application.Current.Resources["ErrorBrush"];
                }
            }
            return Brushes.Transparent;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
