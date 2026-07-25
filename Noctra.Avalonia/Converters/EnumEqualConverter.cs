using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace Noctra.Avalonia.Converters;

public sealed class EnumEqualConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value == null || parameter == null)
        {
            return false;
        }

        string valString = value.ToString() ?? string.Empty;
        string paramString = parameter.ToString() ?? string.Empty;

        return string.Equals(valString, paramString, StringComparison.OrdinalIgnoreCase);
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
