using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace Noctra.Mobile.Converters;

public sealed class WidthToColumnsConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not double width)
        {
            return 1;
        }

        if (width < 520) return 1;
        if (width < 780) return 2;
        if (width < 1080) return 3;
        if (width < 1380) return 4;
        if (width < 1680) return 5;
        return 6;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => null;
}
