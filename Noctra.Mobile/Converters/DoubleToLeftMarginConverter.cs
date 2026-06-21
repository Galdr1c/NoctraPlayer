using System;
using System.Globalization;
using Avalonia;
using Avalonia.Data.Converters;

namespace Noctra.Mobile.Converters;

/// <summary>
/// double pixelLeft → Thickness(pixelLeft, 0, 0, 0)
/// EPG program bloklarını Grid+Margin ile mutlak konumlandırmak için.
/// </summary>
public class DoubleToLeftMarginConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is double d ? new Thickness(Math.Max(0, d), 0, 0, 0) : new Thickness(0);

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotImplementedException();
}
