using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace Noctra.Mobile.Converters;

public sealed class DoubleToFloatConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (TryConvert(value, out var converted))
        {
            return converted;
        }

        if (TryConvert(parameter, out var fallback))
        {
            return fallback;
        }

        return 1.0f;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();

    private static bool TryConvert(object? value, out float result)
    {
        if (value is float floatValue)
        {
            result = floatValue;
            return true;
        }

        if (value is double doubleValue)
        {
            result = (float)doubleValue;
            return true;
        }

        if (value is not null &&
            float.TryParse(value.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out result))
        {
            return true;
        }

        result = 0f;
        return false;
    }
}
