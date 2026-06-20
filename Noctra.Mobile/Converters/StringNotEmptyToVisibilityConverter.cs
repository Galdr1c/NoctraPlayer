using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace Noctra.Mobile.Converters;

public sealed class StringNotEmptyToVisibilityConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is string text)
        {
            var invert = parameter is string s && s.Equals("invert", StringComparison.OrdinalIgnoreCase);
            var isNotEmpty = !string.IsNullOrWhiteSpace(text);
            return invert ? !isNotEmpty : isNotEmpty;
        }

        return false;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
