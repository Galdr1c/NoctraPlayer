using System;
using System.Globalization;
using Avalonia;
using Avalonia.Data.Converters;
using Avalonia.Media;
using Avalonia.Styling;

namespace Noctra.Mobile.Converters;

public sealed class PinDotConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var pinLength = value is int length ? length : 0;
        _ = int.TryParse(parameter?.ToString(), out var dotIndex);
        var filled = pinLength >= dotIndex;

        if (filled &&
            Application.Current?.TryGetResource("AccentBrush", ThemeVariant.Default, out var accentResource) == true &&
            accentResource is IBrush accentBrush)
        {
            return accentBrush;
        }

        if (Application.Current?.TryGetResource("BorderBrush", ThemeVariant.Default, out var borderResource) == true &&
            borderResource is IBrush borderBrush)
        {
            return borderBrush;
        }

        return filled
            ? new SolidColorBrush(Color.Parse("#8B5CF6"))
            : new SolidColorBrush(Color.Parse("#333333"));
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => null;
}
