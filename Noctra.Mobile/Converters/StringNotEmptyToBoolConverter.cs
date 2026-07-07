using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace Noctra.Mobile.Converters;

public sealed class StringNotEmptyToBoolConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return !string.IsNullOrWhiteSpace(value as string);
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => null;
}
