using System;
using System.Globalization;
using System.Windows.Data;

namespace RansomGuard.Converters
{
    public class BooleanToStatusConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool b && parameter is string param)
            {
                var parts = param.Split(';');
                if (parts.Length == 2)
                {
                    return b ? parts[0] : parts[1];
                }
            }
            return "UNKNOWN";
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
