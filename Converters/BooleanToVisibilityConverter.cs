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
            bool boolValue = false;
            if (value is bool b) boolValue = b;
            else if (value is int i) boolValue = i > 0;
            else if (value is long l) boolValue = l > 0;

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
