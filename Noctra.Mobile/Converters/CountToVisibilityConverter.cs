using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace Noctra.Mobile.Converters;

public sealed class CountToVisibilityConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is int count)
        {
            var invert = parameter is string s && s.Equals("invert", StringComparison.OrdinalIgnoreCase);
            return invert ? count == 0 : count > 0;
        }

        if (value is System.Collections.ICollection collection)
        {
            var invert = parameter is string s && s.Equals("invert", StringComparison.OrdinalIgnoreCase);
            return invert ? collection.Count == 0 : collection.Count > 0;
        }

        return false;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
