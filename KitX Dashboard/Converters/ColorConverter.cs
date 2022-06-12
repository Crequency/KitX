using Avalonia.Data.Converters;
using Avalonia.Media;
using System;
using System.Globalization;
using FluentAvalonia.UI.Media;

#pragma warning disable CS8602 // 解引用可能出现空引用。

namespace KitX_Dashboard.Converters
{
    internal class ColorConverter : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            return new Color2((value as SolidColorBrush).Color);
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            return null;
        }
    }
}

#pragma warning restore CS8602 // 解引用可能出现空引用。