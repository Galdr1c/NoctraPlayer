using System;
using System.Globalization;
using Avalonia.Controls;
using Avalonia.Data.Converters;

namespace Noctra.Mobile.Converters;

public sealed class DoubleToStarGridLengthConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var weight = value switch
        {
            double doubleValue => doubleValue,
            float floatValue => floatValue,
            int intValue => intValue,
            long longValue => longValue,
            _ when value is not null && double.TryParse(value.ToString(), NumberStyles.Float, culture, out var parsed) => parsed,
            _ => 0d
        };

        return new GridLength(Math.Max(0d, weight), GridUnitType.Star);
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => null;
}
