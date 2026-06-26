using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace Noctra.Mobile.Converters;

public sealed class UrgencyToColorConverter : IValueConverter
{
    private static readonly SolidColorBrush OrangeBrush = new(Color.Parse("#FF8C00"));
    private static readonly SolidColorBrush RedOrangeBrush = new(Color.Parse("#FF4500"));
    private static readonly SolidColorBrush RedBrush = new(Color.Parse("#FF1744"));

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var urgency = value is double numericUrgency ? numericUrgency : 1.0;

        if (urgency >= 0.6)
        {
            return OrangeBrush;
        }

        if (urgency >= 0.3)
        {
            return RedOrangeBrush;
        }

        return RedBrush;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => null;
}
