using Avalonia.Data.Converters;
using System;
using System.Globalization;

#pragma warning disable CS8605 // 取消装箱可能为 null 的值。

namespace KitX_Dashboard.Converters
{
    internal class Selection2VisibleConverter : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
            (int)value == 0;

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
            (bool)value ? 0 : 1;
    }
}

#pragma warning restore CS8605 // 取消装箱可能为 null 的值。
