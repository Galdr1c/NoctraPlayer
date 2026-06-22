using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace Noctra.Mobile.Converters;

public sealed class SubtitleBackgroundBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var opacity = 0;

        if (value is int intValue)
        {
            opacity = intValue;
        }
        else if (value is string stringValue && int.TryParse(stringValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedValue))
        {
            opacity = parsedValue;
        }

        opacity = Math.Clamp(opacity, 0, 255);
        return new SolidColorBrush(Color.FromArgb((byte)opacity, 0, 0, 0));
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
