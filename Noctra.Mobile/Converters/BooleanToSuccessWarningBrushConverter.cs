using System;
using System.Globalization;
using Avalonia;
using Avalonia.Data.Converters;
using Avalonia.Media;
using Avalonia.Styling;

namespace Noctra.Mobile.Converters;

public sealed class BooleanToSuccessWarningBrushConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var isSuccess = value is bool boolValue && boolValue;
        var resourceKey = isSuccess ? "SuccessBrush" : "WarningBrush";

        if (Application.Current?.TryGetResource(resourceKey, ThemeVariant.Default, out var resource) == true &&
            resource is IBrush brush)
        {
            return brush;
        }

        return isSuccess ? Brushes.Green : Brushes.Orange;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => null;
}
