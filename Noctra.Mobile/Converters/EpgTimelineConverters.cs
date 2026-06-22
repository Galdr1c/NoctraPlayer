using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace Noctra.Mobile.Converters;

/// <summary>
/// EPG program bloğu için desktop ile uyumlu renk seçimi.
/// Current program daha belirgin, diğer programlar daha sakin görünür.
/// </summary>
public class BoolToEpgBlockColorConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is true
            ? Color.FromArgb(92, 123, 47, 190)
            : Color.FromArgb(38, 255, 255, 255);

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

/// <summary>
/// EPG program bloğunun kenarlığını current program için güçlendirir.
/// </summary>
public class BoolToEpgBorderColorConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is true
            ? Color.FromArgb(220, 123, 47, 190)
            : Color.FromArgb(45, 255, 255, 255);

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

/// <summary>
/// EPG satırlarında aktif kanal vurgusu için ortak brush üretir.
/// </summary>
public class BoolToSelectionBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is true
            ? new SolidColorBrush(Color.FromArgb(36, 123, 47, 190))
            : new SolidColorBrush(Colors.Transparent);

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotImplementedException();
}
