using System;
using System.Globalization;
using Avalonia;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace Noctra.Mobile.Converters;

public sealed class ProfileColorConverter : IValueConverter
{
    private static readonly IBrush[] Brushes =
    [
        CreateGradient("#E50914", "#B81D24"),
        CreateGradient("#1A73E8", "#0D47A1"),
        CreateGradient("#0F9D58", "#1B5E20"),
        CreateGradient("#F4B400", "#FF6F00"),
        CreateGradient("#AB47BC", "#6A1B9A"),
        CreateGradient("#26C6DA", "#0097A7"),
        CreateGradient("#FF7043", "#D84315")
    ];

    private static LinearGradientBrush CreateGradient(string startColor, string endColor)
        => new()
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
            GradientStops =
            [
                new GradientStop(Color.Parse(startColor), 0.0),
                new GradientStop(Color.Parse(endColor), 1.0)
            ]
        };

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is string name && !string.IsNullOrWhiteSpace(name))
        {
            var index = Math.Abs(name.GetHashCode()) % Brushes.Length;
            return Brushes[index];
        }

        return Brushes[0];
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => null;
}
