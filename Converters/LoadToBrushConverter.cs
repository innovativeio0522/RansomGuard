using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace RansomGuard.Converters
{
    public class LoadToBrushConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            double load = 0;
            if (value is double d) load = d;
            else if (value != null && double.TryParse(value.ToString(), out double parsed)) load = parsed;

            string key = load switch
            {
                < 20 => "SecondaryBrush",  // Nominal (Green)
                < 70 => "PrimaryBrush",    // Active (Blue)
                _ => "TertiaryBrush"       // High Load (Pink/Red)
            };

            if (Application.Current?.Resources.Contains(key) == true)
            {
                return Application.Current.Resources[key];
            }

            return Brushes.Gray;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
