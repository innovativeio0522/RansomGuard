using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace RansomGuard.Converters
{
    public class IoToBrushConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is double io && io > 0)
            {
                return Application.Current.TryFindResource("SecondaryBrush") ?? Brushes.Green;
            }
            return Application.Current.TryFindResource("OutlineVariantBrush") ?? Brushes.Gray;
        }
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotImplementedException();
    }

    public class IoToStatusConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is double io && io > 0) return "ACTIVE";
            return "IDLE";
        }
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotImplementedException();
    }

    public class TrustToBrushConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool trusted && trusted)
            {
                return Application.Current.TryFindResource("SecondaryBrush") ?? Brushes.Green;
            }
            return Application.Current.TryFindResource("TertiaryBrush") ?? Brushes.Red;
        }
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotImplementedException();
    }

    public class TrustToTextConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool trusted && trusted) return "TRUSTED";
            return "UNKNOWN";
        }
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotImplementedException();
    }
}
