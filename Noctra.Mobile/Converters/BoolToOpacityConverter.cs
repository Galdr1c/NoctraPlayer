using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace Noctra.Mobile.Converters;

/// <summary>
/// bool → double opacity. true ise ConverterParameter'daki değer (varsayılan 0.4), false ise 1.0.
/// </summary>
public class BoolToOpacityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not true)
            return 1.0;

        var dimmed = 0.4;
        if (parameter is string s && double.TryParse(s, NumberStyles.Float,
            CultureInfo.InvariantCulture, out var parsed))
            dimmed = parsed;

        return dimmed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotImplementedException();
}
