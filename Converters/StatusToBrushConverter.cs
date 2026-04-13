using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace RansomGuard.Converters
{
    /// <summary>
    /// Maps status strings to brushes:
    ///   "PASS" / "STABLE" → SecondaryBrush (green)
    ///   "ALERT"           → TertiaryBrush  (red/orange)
    ///   anything else     → OnSurfaceVariantBrush
    /// </summary>
    public class StatusToBrushConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is string status)
            {
                string resourceKey = status switch
                {
                    "PASS" or "STABLE" => "SecondaryBrush",
                    "ALERT"            => "TertiaryBrush",
                    _                  => "OnSurfaceVariantBrush"
                };

                if (Application.Current?.Resources.Contains(resourceKey) == true)
                    return Application.Current.Resources[resourceKey];
            }
            
            // Return neutral brush instead of transparent
            return Application.Current?.TryFindResource("OnSurfaceVariantBrush") ?? Brushes.Gray;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }
}
