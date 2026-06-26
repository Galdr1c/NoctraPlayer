using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Material.Icons;

namespace Noctra.Mobile.Converters;

public sealed class BoolToMaterialIconKindConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var boolValue = value is bool b && b;
        var parts = (parameter as string)?.Split('|');

        if (parts is not { Length: 2 })
        {
            return null;
        }

        return Enum.TryParse<MaterialIconKind>(boolValue ? parts[0] : parts[1], true, out var kind)
            ? kind
            : null;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => null;
}
