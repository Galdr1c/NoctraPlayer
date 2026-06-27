using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace Noctra.Mobile.Converters;

public sealed class BytesToHumanConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var bytes = value switch
        {
            long longValue => longValue,
            int intValue => intValue,
            double doubleValue => (long)doubleValue,
            _ when value is not null && long.TryParse(value.ToString(), NumberStyles.Integer, culture, out var parsed) => parsed,
            _ => 0
        };

        if (bytes <= 0)
        {
            return "0 B";
        }

        string[] units = ["B", "KB", "MB", "GB", "TB"];
        var size = (double)bytes;
        var unitIndex = 0;

        while (size >= 1024 && unitIndex < units.Length - 1)
        {
            size /= 1024;
            unitIndex++;
        }

        return string.Format(culture, "{0:N2} {1}", size, units[unitIndex]);
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => null;
}
