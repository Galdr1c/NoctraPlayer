using System;
using System.Collections;
using System.Globalization;
using Avalonia.Data.Converters;
using Noctra.Mobile.Localization;

namespace Noctra.Mobile.Converters;

public sealed class StringFormatConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (parameter is null)
        {
            return value?.ToString();
        }

        var format = parameter.ToString();
        if (string.IsNullOrEmpty(format))
        {
            return value?.ToString();
        }

        if (!format.Contains(' ') && format.Contains('.'))
        {
            var translated = LocalizationSource.Instance[format];
            if (translated != format)
            {
                format = translated;
            }
        }

        try
        {
            if (value is ICollection collection)
            {
                return string.Format(culture, format, collection.Count);
            }

            return string.Format(culture, format, value);
        }
        catch
        {
            return value?.ToString() ?? string.Empty;
        }
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => null;
}
