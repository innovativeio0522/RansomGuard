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
                    try
                    {
                        return Application.Current.FindResource(brushName);
                    }
                    catch
                    {
                        // Fallback to neutral brush if resource not found
                        return Application.Current.TryFindResource("OnSurfaceBrush") ?? Brushes.Gray;
                    }
                }
                // Default fallback for true without parameter - use neutral brush
                return Application.Current.TryFindResource("OnSurfaceBrush") ?? Brushes.Gray;
            }
            
            return Application.Current.TryFindResource("OutlineVariantBrush") ?? Brushes.LightGray;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
