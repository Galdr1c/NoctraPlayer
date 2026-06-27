using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;
using Noctra.Models;

namespace Noctra.Mobile.Converters;

public sealed class ConnectionHealthToBrushConverter : IValueConverter
{
    private static readonly IBrush GoodBrush = new SolidColorBrush(Color.Parse("#4CAF50"));
    private static readonly IBrush WeakBrush = new SolidColorBrush(Color.Parse("#FFC107"));
    private static readonly IBrush BadBrush = new SolidColorBrush(Color.Parse("#FF5722"));
    private static readonly IBrush CriticalBrush = new SolidColorBrush(Color.Parse("#F44336"));
    private static readonly IBrush UnknownBrush = new SolidColorBrush(Color.Parse("#9E9E9E"));

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is ConnectionHealth health
            ? health switch
            {
                ConnectionHealth.Good => GoodBrush,
                ConnectionHealth.Weak => WeakBrush,
                ConnectionHealth.Bad => BadBrush,
                ConnectionHealth.Critical => CriticalBrush,
                _ => UnknownBrush
            }
            : UnknownBrush;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => null;
}

public sealed class ConnectionHealthToIconConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is ConnectionHealth health
            ? health switch
            {
                ConnectionHealth.Good => "CheckCircle",
                ConnectionHealth.Weak => "AlertCircle",
                ConnectionHealth.Bad => "Alert",
                ConnectionHealth.Critical => "CloseCircle",
                _ => "HelpCircle"
            }
            : "HelpCircle";

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => null;
}

public sealed class ConnectionHealthToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is ConnectionHealth health && health != ConnectionHealth.Unknown;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => null;
}
