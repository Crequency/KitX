using Avalonia.Data.Converters;
using System;
using System.Globalization;

namespace KitX_Dashboard.Converters
{
    internal class StringOverLengthConverter : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value != null && parameter != null)
            {
                string str = (string)value;
                int length = int.Parse((string)parameter);
                if (str.Length > length)
                    return string.Concat(str.AsSpan(0, length), " ...");
                else return str;
            }
            else return string.Empty;
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            return value;
        }
    }
}
