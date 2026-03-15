using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace Noctra.Avalonia.Converters;

public class EqualityToOpacityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value == null || parameter == null)
            return 0.0;

        return value.Equals(parameter) ? 1.0 : 0.0;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}