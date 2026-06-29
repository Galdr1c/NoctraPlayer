using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Noctra.Mobile.Localization;

namespace Noctra.Mobile.Converters;

public sealed class BoolToLocalizedStringConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var parts = (parameter as string)?.Split('|', 2);
        if (parts is not { Length: 2 })
        {
            return string.Empty;
        }

        var key = value is bool boolValue && boolValue ? parts[0] : parts[1];
        return LocalizationSource.Instance[key];
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => null;
}
