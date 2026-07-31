using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;
using Noctra.Models;

namespace Noctra.Mobile.Converters;

public sealed class SubtitleBackgroundBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var percent = 0;

        if (value is int intValue)
        {
            percent = intValue;
        }
        else if (value is string stringValue && int.TryParse(stringValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedValue))
        {
            percent = parsedValue;
        }

        var alpha = SubtitleAppearanceDefaults.OpacityPercentToAlpha(percent);
        return new SolidColorBrush(Color.FromArgb(alpha, 0, 0, 0));
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
