using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace RansomGuard.Converters
{
    public class BooleanToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            bool boolValue = (bool)value;
            bool isInverse = parameter != null && parameter.ToString() == "Inverse";

            if (isInverse)
            {
                return boolValue ? Visibility.Collapsed : Visibility.Visible;
            }

            return boolValue ? Visibility.Visible : Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
