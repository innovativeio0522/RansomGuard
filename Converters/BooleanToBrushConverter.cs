using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace RansomGuard.Converters
{
    public class BooleanToBrushConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool boolValue && boolValue)
            {
                // If it's a string resource name passed as parameter
                if (parameter is string brushName)
                {
                    return Application.Current.FindResource(brushName);
                }
                return Brushes.Red; // Default fallback
            }
            
            return Application.Current.FindResource("OutlineVariantBrush");
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
