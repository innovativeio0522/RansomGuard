using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace RansomGuard.Converters
{
    /// <summary>
    /// Maps input string to a brush based on a semicolon-separated parameter:
    /// "KEY1:COLOR1;KEY2:COLOR2"
    /// Example: "ARMED:#4edea3;TRIPPED:#ff5252"
    /// </summary>
    public class StringToBrushConverter : IValueConverter
    {
        private static readonly BrushConverter _brushConverter = new();

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is not string input || parameter is not string mapping)
                return Brushes.Gray;

            var pairs = mapping.Split(';')
                               .Select(p => p.Split(':'))
                               .Where(p => p.Length == 2)
                               .ToDictionary(p => p[0].Trim(), p => p[1].Trim(), StringComparer.OrdinalIgnoreCase);

            if (pairs.TryGetValue(input.Trim(), out var colorStr))
            {
                try
                {
                    // Check if it's a resource key first
                    if (Application.Current?.Resources.Contains(colorStr) == true)
                        return Application.Current.Resources[colorStr];

                    // Otherwise try to parse as hex/color name
                    return _brushConverter.ConvertFromString(colorStr) ?? Brushes.Gray;
                }
                catch
                {
                    return Brushes.Gray;
                }
            }

            return Brushes.Gray;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }
}
